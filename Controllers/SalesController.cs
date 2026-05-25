using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using SmartStockERP.Filters;
using SmartStockERP.Utilities;

namespace SmartStockERP.Controllers
{
    [AuthCheck]
    public class SalesController : Controller
    {
        private readonly SendGridEmailService _emailService;

        public SalesController(SendGridEmailService emailService)
        {
            _emailService = emailService;
        }

        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("CompanyId") == null)
                return RedirectToAction("Index", "Login");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateSale(int customerId, string items)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();
                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    string invoiceNo =
                        $"INV-{DateTime.Now:yyyyMMdd}-{DateTime.Now.Ticks.ToString().Substring(10)}";

                    SqlCommand cmd = new SqlCommand(@"
                        INSERT INTO Sales (CompanyId, CustomerId, TotalAmount, InvoiceNo)
                        OUTPUT INSERTED.SaleId
                        VALUES (@CompanyId,@CustomerId,0,@InvoiceNo)", con, tx);

                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    cmd.Parameters.AddWithValue("@CustomerId", customerId);
                    cmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);

                    int saleId = (int)cmd.ExecuteScalar();

                    decimal total = 0;

                    var itemList = JsonConvert.DeserializeObject<List<dynamic>>(items);

                    foreach (var item in itemList)
                    {
                        int productId = Convert.ToInt32(item.productId);
                        int qty = Convert.ToInt32(item.qty);

                        SqlCommand stockCheck = new SqlCommand(@"
                            SELECT StockQty FROM Products
                            WHERE ProductId=@Id AND CompanyId=@CompanyId", con, tx);

                        stockCheck.Parameters.AddWithValue("@Id", productId);
                        stockCheck.Parameters.AddWithValue("@CompanyId", companyId);

                        int stock = Convert.ToInt32(stockCheck.ExecuteScalar());

                        if (qty > stock)
                            throw new Exception("Insufficient stock");

                        SqlCommand priceCmd = new SqlCommand(@"
                            SELECT Price FROM Products
                            WHERE ProductId=@Id AND CompanyId=@CompanyId", con, tx);

                        priceCmd.Parameters.AddWithValue("@Id", productId);
                        priceCmd.Parameters.AddWithValue("@CompanyId", companyId);

                        decimal price = Convert.ToDecimal(priceCmd.ExecuteScalar());

                        decimal lineTotal = price * qty;
                        total += lineTotal;

                        SqlCommand itemCmd = new SqlCommand(@"
                            INSERT INTO SaleItems
                            (SaleId, ProductId, Qty, Price, CompanyId)
                            VALUES (@SaleId,@ProductId,@Qty,@Price,@CompanyId)", con, tx);

                        itemCmd.Parameters.AddWithValue("@SaleId", saleId);
                        itemCmd.Parameters.AddWithValue("@ProductId", productId);
                        itemCmd.Parameters.AddWithValue("@Qty", qty);
                        itemCmd.Parameters.AddWithValue("@Price", price);
                        itemCmd.Parameters.AddWithValue("@CompanyId", companyId);

                        itemCmd.ExecuteNonQuery();

                        SqlCommand stockCmd = new SqlCommand(@"
                            UPDATE Products
                            SET StockQty = StockQty - @Qty
                            WHERE ProductId=@Id AND CompanyId=@CompanyId", con, tx);

                        stockCmd.Parameters.AddWithValue("@Qty", qty);
                        stockCmd.Parameters.AddWithValue("@Id", productId);
                        stockCmd.Parameters.AddWithValue("@CompanyId", companyId);

                        stockCmd.ExecuteNonQuery();
                    }

                    SqlCommand totalCmd = new SqlCommand(@"
                        UPDATE Sales SET TotalAmount=@Total
                        WHERE SaleId=@SaleId AND CompanyId=@CompanyId", con, tx);

                    totalCmd.Parameters.AddWithValue("@Total", total);
                    totalCmd.Parameters.AddWithValue("@SaleId", saleId);
                    totalCmd.Parameters.AddWithValue("@CompanyId", companyId);

                    totalCmd.ExecuteNonQuery();

                    SqlCommand lowCmd = new SqlCommand(@"
                    SELECT ProductName, StockQty
                    FROM Products
                    WHERE CompanyId=@cid
                    AND StockQty < 5", con, tx);

                    lowCmd.Parameters.AddWithValue("@cid", companyId);

                    SqlDataReader dr = lowCmd.ExecuteReader();

                    List<string> alerts = new();

                    while (dr.Read())
                    {
                        alerts.Add($"{dr["ProductName"]} stock low: {dr["StockQty"]}");
                    }
                    dr.Close();

                    if (alerts.Count > 0)
                    {
                        string emailBody = string.Join("<br>", alerts);

                        string adminEmail = GetAdminEmail(companyId);

                        if (!string.IsNullOrEmpty(adminEmail))
                        {
                            await _emailService.SendEmailAsync(
                                adminEmail,
                                "Admin",
                                "🚨 Low Stock Alert (Below 5)",
                                emailBody
                            );
                        }
                    }


                    tx.Commit();

                    return Ok(new { saleId, invoiceNo, total });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        [HttpGet]
        public IActionResult Invoice(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                // SALE INFO
                SqlCommand saleCmd = new SqlCommand(@"
        SELECT 
            s.InvoiceNo,
            s.TotalAmount,
            s.SaleDate,
            c.CustomerName,
            c.Phone,
            c.Email
        FROM Sales s
        JOIN Customers c 
            ON s.CustomerId = c.CustomerId
        WHERE s.SaleId=@Id
        AND s.CompanyId=@CompanyId", con);

                saleCmd.Parameters.AddWithValue("@Id", id);
                saleCmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = saleCmd.ExecuteReader();

                string invoiceNo = "";
                string customer = "";
                string phone = "";
                string email = "";

                decimal total = 0;
                DateTime date = DateTime.Now;

                if (dr.Read())
                {
                    invoiceNo = dr["InvoiceNo"].ToString();
                    customer = dr["CustomerName"].ToString();
                    phone = dr["Phone"].ToString();
                    email = dr["Email"].ToString();

                    total = Convert.ToDecimal(dr["TotalAmount"]);

                    date = Convert.ToDateTime(dr["SaleDate"]);
                }

                dr.Close();

                // ITEMS
                SqlCommand itemCmd = new SqlCommand(@"
       SELECT 
    p.ProductName,
    p.Unit,
    ISNULL(p.CategoryName,'No Category') AS CategoryName,
    si.Qty,
    si.Price,
    (si.Qty * si.Price) AS LineTotal

FROM SaleItems si

JOIN Products p 
    ON si.ProductId = p.ProductId

LEFT JOIN Categories c
    ON p.CategoryId = c.CategoryId

WHERE si.SaleId=@Id
AND si.CompanyId=@CompanyId", con);

                itemCmd.Parameters.AddWithValue("@Id", id);

                // IMPORTANT FIX
                itemCmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr2 = itemCmd.ExecuteReader();

                List<object> items = new List<object>();

                while (dr2.Read())
                {
                    items.Add(new
                    {
                        productName = dr2["ProductName"].ToString(),
                        category = dr2["CategoryName"].ToString(),
                        unit = dr2["Unit"].ToString(),
                        qty = dr2["Qty"],
                        price = dr2["Price"],
                        totalLine = dr2["LineTotal"]
                    });
                }

                return Json(new
                {
                    invoiceNo,
                    customer,
                    phone,
                    email,
                    total,
                    date = date.ToString("dd MMM yyyy"),
                    items
                });
            }
        }

        public IActionResult History()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetSales()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
                    SELECT s.SaleId, s.InvoiceNo, s.TotalAmount, s.SaleDate, c.CustomerName
                    FROM Sales s
                    JOIN Customers c ON s.CustomerId = c.CustomerId
                    WHERE s.CompanyId=@CompanyId
                    ORDER BY s.SaleId DESC", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        saleId = dr["SaleId"],
                        invoiceNo = dr["InvoiceNo"],
                        customer = dr["CustomerName"],
                        total = dr["TotalAmount"],
                        date = Convert.ToDateTime(dr["SaleDate"]).ToString("dd MMM yyyy")
                    });
                }
            }

            return Json(list);
        }

        [HttpPost]
        public IActionResult DeleteSale(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();
                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    SqlCommand itemCmd = new SqlCommand(@"
                        SELECT ProductId, Qty FROM SaleItems
                        WHERE SaleId=@SaleId AND CompanyId=@CompanyId", con, tx);

                    itemCmd.Parameters.AddWithValue("@SaleId", id);
                    itemCmd.Parameters.AddWithValue("@CompanyId", companyId);

                    SqlDataReader dr = itemCmd.ExecuteReader();

                    List<(int pid, int qty)> items = new();

                    while (dr.Read())
                        items.Add((Convert.ToInt32(dr["ProductId"]), Convert.ToInt32(dr["Qty"])));

                    dr.Close();

                    foreach (var it in items)
                    {
                        SqlCommand restore = new SqlCommand(@"
                            UPDATE Products
                            SET StockQty = StockQty + @Qty
                            WHERE ProductId=@Id AND CompanyId=@CompanyId", con, tx);

                        restore.Parameters.AddWithValue("@Qty", it.qty);
                        restore.Parameters.AddWithValue("@Id", it.pid);
                        restore.Parameters.AddWithValue("@CompanyId", companyId);

                        restore.ExecuteNonQuery();
                    }

                    new SqlCommand("DELETE FROM SaleItems WHERE SaleId=@SaleId AND CompanyId=@CompanyId", con, tx)
                    {
                        Parameters =
                        {
                            new SqlParameter("@SaleId", id),
                            new SqlParameter("@CompanyId", companyId)
                        }
                    }.ExecuteNonQuery();

                    new SqlCommand("DELETE FROM Sales WHERE SaleId=@SaleId AND CompanyId=@CompanyId", con, tx)
                    {
                        Parameters =
                        {
                            new SqlParameter("@SaleId", id),
                            new SqlParameter("@CompanyId", companyId)
                        }
                    }.ExecuteNonQuery();

                    tx.Commit();

                    return Ok();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        [HttpGet]
        public IActionResult GetDropdown()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new List<object>();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
SELECT 
    p.ProductId,
    p.ProductName,
    p.Price,
    p.StockQty,
    p.Unit,
    ISNULL(p.CategoryName,'No Category') AS CategoryName
FROM Products p
LEFT JOIN Categories c
    ON p.CategoryId = c.CategoryId
WHERE p.CompanyId=@CompanyId
ORDER BY p.ProductName", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        id = dr["ProductId"],
                        name = dr["ProductName"],
                        price = dr["Price"],
                        stock = dr["StockQty"],
                        unit = dr["Unit"],
                        category = dr["CategoryName"]
                    });
                }
            }

            return Json(list);
        }

        [HttpGet]
        public IActionResult GetCustomerDropdown()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT CustomerId, CustomerName
            FROM Customers
            WHERE CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        id = dr["CustomerId"],
                        name = dr["CustomerName"]
                    });
                }
            }

            return Json(list);
        }

        public IActionResult InvoicePdf(int id)
        {
            MemoryStream ms = new MemoryStream();

            Document doc = new Document(PageSize.A4, 30, 30, 40, 40);

            PdfWriter.GetInstance(doc, ms);

            doc.Open();

            // =========================================================
            // COLORS
            // =========================================================

            BaseColor primary = new BaseColor(107, 127, 178);      // #6b7fb2
            BaseColor primaryDark = new BaseColor(79, 99, 150);    // #4f6396
            BaseColor primaryLight = new BaseColor(152, 181, 221); // #98b5dd

            BaseColor light = new BaseColor(245, 247, 252);
            BaseColor border = new BaseColor(220, 220, 220);

            // =========================================================
            // FONTS
            // =========================================================

            Font titleFont = FontFactory.GetFont(
                FontFactory.HELVETICA_BOLD,
                22,
                BaseColor.WHITE
            );

            Font headingFont = FontFactory.GetFont(
                FontFactory.HELVETICA_BOLD,
                13,
                primaryDark
            );

            Font normalFont = FontFactory.GetFont(
                FontFactory.HELVETICA,
                11,
                BaseColor.BLACK
            );

            Font boldFont = FontFactory.GetFont(
                FontFactory.HELVETICA_BOLD,
                11,
                BaseColor.BLACK
            );

            Font whiteFont = FontFactory.GetFont(
                FontFactory.HELVETICA_BOLD,
                11,
                BaseColor.WHITE
            );

            // =========================================================
            // VARIABLES
            // =========================================================

            string invoiceNo = "";
            string customerName = "";
            string phone = "";
            string email = "";

            decimal totalAmount = 0;

            DateTime saleDate = DateTime.Now;

            List<dynamic> items = new List<dynamic>();

            // =========================================================
            // DATABASE
            // =========================================================

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                // =====================================================
                // SALE INFO
                // =====================================================

                SqlCommand cmd = new SqlCommand(@"
        SELECT 
            s.InvoiceNo,
            s.TotalAmount,
            s.SaleDate,
            c.CustomerName,
            c.Phone,
            c.Email
        FROM Sales s
        JOIN Customers c 
            ON s.CustomerId = c.CustomerId
        WHERE s.SaleId=@Id", con);

                cmd.Parameters.AddWithValue("@Id", id);

                SqlDataReader dr = cmd.ExecuteReader();

                if (dr.Read())
                {
                    invoiceNo = dr["InvoiceNo"].ToString();

                    customerName = dr["CustomerName"].ToString();

                    totalAmount = Convert.ToDecimal(dr["TotalAmount"]);

                    saleDate = Convert.ToDateTime(dr["SaleDate"]);

                    phone = dr["Phone"].ToString();

                    email = dr["Email"].ToString();
                }

                dr.Close();

                // =====================================================
                // SALE ITEMS
                // =====================================================

                SqlCommand itemCmd = new SqlCommand(@"
       SELECT 
    p.ProductName,
    p.Unit,
    ISNULL(p.CategoryName,'No Category') AS CategoryName,
    si.Qty,
    si.Price,
    (si.Qty * si.Price) AS LineTotal
        FROM SaleItems si
        JOIN Products p 
            ON si.ProductId = p.ProductId
LEFT JOIN Categories c
    ON p.CategoryId = c.CategoryId
        WHERE si.SaleId=@Id", con);

                itemCmd.Parameters.AddWithValue("@Id", id);

                SqlDataReader dr2 = itemCmd.ExecuteReader();

                while (dr2.Read())
                {
                    items.Add(new
                    {
                        Product = dr2["ProductName"].ToString(),

                        Category = dr2["CategoryName"].ToString(),

                        Unit = dr2["Unit"].ToString(),

                        Qty = Convert.ToInt32(dr2["Qty"]),

                        Price = Convert.ToDecimal(dr2["Price"]),

                        Total = Convert.ToDecimal(dr2["LineTotal"])
                    });
                }

                dr2.Close();

                // =====================================================
                // HEADER
                // =====================================================

                PdfPTable header = new PdfPTable(2);

                header.WidthPercentage = 100;

                header.SetWidths(new float[] { 70, 30 });

                // LEFT HEADER

                PdfPCell leftHeader = new PdfPCell();

                leftHeader.Border = 0;

                leftHeader.BackgroundColor = primaryLight;

                leftHeader.Padding = 18;

                leftHeader.AddElement(
                    new Paragraph("SMART STOCK ERP", titleFont)
                );

                leftHeader.AddElement(
                    new Paragraph("Professional Sales Invoice", whiteFont)
                );

                // RIGHT HEADER

                PdfPCell rightHeader = new PdfPCell();

                rightHeader.Border = 0;

                rightHeader.BackgroundColor = primaryDark;

                rightHeader.Padding = 18;

                rightHeader.HorizontalAlignment = Element.ALIGN_RIGHT;

                Paragraph inv = new Paragraph("INVOICE", titleFont);

                inv.Alignment = Element.ALIGN_RIGHT;

                rightHeader.AddElement(inv);

                header.AddCell(leftHeader);

                header.AddCell(rightHeader);

                doc.Add(header);

                // =====================================================
                // COMPANY INFO
                // =====================================================

                Paragraph companyInfo = new Paragraph(
                    "Smart Stock ERP Pvt Ltd\n" +
                    "Indore, Madhya Pradesh\n" +
                    "Phone: +91 9876543210\n" +
                    "Email: support@smartstockerp.com\n\n",
                    FontFactory.GetFont(
                        FontFactory.HELVETICA,
                        10,
                        BaseColor.DARK_GRAY
                    )
                );

                companyInfo.Alignment = Element.ALIGN_LEFT;

                doc.Add(companyInfo);

                // =====================================================
                // CUSTOMER + INVOICE DETAILS
                // =====================================================

                PdfPTable infoTable = new PdfPTable(2);

                infoTable.WidthPercentage = 100;

                infoTable.SetWidths(new float[] { 50, 50 });

                // CUSTOMER BOX

                PdfPCell customerCell = new PdfPCell();

                customerCell.Padding = 14;

                customerCell.BorderColor = border;

                customerCell.BackgroundColor = new BaseColor(248, 250, 255);

                customerCell.AddElement(
                    new Paragraph("BILL TO", headingFont)
                );

                customerCell.AddElement(new Paragraph(" "));

                customerCell.AddElement(
                    new Paragraph(customerName, boldFont)
                );

                customerCell.AddElement(
                    new Paragraph("Phone: " + phone, normalFont)
                );

                customerCell.AddElement(
                    new Paragraph("Email: " + email, normalFont)
                );

                // INVOICE BOX

                PdfPCell invoiceCell = new PdfPCell();

                invoiceCell.Padding = 14;

                invoiceCell.BorderColor = border;

                invoiceCell.BackgroundColor = new BaseColor(248, 250, 255);

                invoiceCell.AddElement(
                    new Paragraph("INVOICE DETAILS", headingFont)
                );

                invoiceCell.AddElement(new Paragraph(" "));

                invoiceCell.AddElement(
                    new Paragraph("Invoice No: " + invoiceNo, boldFont)
                );

                invoiceCell.AddElement(
                    new Paragraph(
                        "Invoice Date: " + saleDate.ToString("dd MMM yyyy"),
                        normalFont
                    )
                );

                invoiceCell.AddElement(
                    new Paragraph("Payment Status: PAID", normalFont)
                );

                infoTable.AddCell(customerCell);

                infoTable.AddCell(invoiceCell);

                doc.Add(infoTable);

                doc.Add(new Paragraph(" "));
                doc.Add(new Paragraph(" "));

                // =====================================================
                // ITEMS TABLE
                // =====================================================

                PdfPTable table = new PdfPTable(7);

                table.WidthPercentage = 100;

                table.SetWidths(new float[] { 8, 25, 20, 12, 10, 12, 13 });

                // =====================================================
                // TABLE HEADER
                // =====================================================

                string[] headers =
 {
    "Sr No",
    "Product",
    "Category",
    "Unit",
    "Qty",
    "Price",
    "Total"
};

                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(
                        new Phrase(h, whiteFont)
                    );

                    cell.BackgroundColor = primaryDark;

                    cell.Padding = 10;

                    cell.HorizontalAlignment = Element.ALIGN_CENTER;

                    table.AddCell(cell);
                }

                // =====================================================
                // TABLE ROWS
                // =====================================================

                int index = 1;

                int totalQty = 0;

                foreach (var item in items)
                {
                    totalQty += item.Qty;

                    BaseColor rowColor =
                        index % 2 == 0
                        ? new BaseColor(245, 247, 252)
                        : BaseColor.WHITE;

                    PdfPCell c1 = new PdfPCell(
     new Phrase(index.ToString(), normalFont)
 );

                    PdfPCell c2 = new PdfPCell(
                        new Phrase(item.Product, normalFont)
                    );

                    PdfPCell c3 = new PdfPCell(
                        new Phrase(item.Category, normalFont)
                    );

                    PdfPCell c4 = new PdfPCell(
                        new Phrase(item.Unit, normalFont)
                    );

                    PdfPCell c5 = new PdfPCell(
                        new Phrase(item.Qty.ToString(), normalFont)
                    );

                    PdfPCell c6 = new PdfPCell(
                        new Phrase("₹ " + item.Price.ToString("0.00"), normalFont)
                    );

                    PdfPCell c7 = new PdfPCell(
                        new Phrase("₹ " + item.Total.ToString("0.00"), boldFont)
                    );

                    c1.BackgroundColor = rowColor;
                    c2.BackgroundColor = rowColor;
                    c3.BackgroundColor = rowColor;
                    c4.BackgroundColor = rowColor;
                    c5.BackgroundColor = rowColor;
                    c6.BackgroundColor = rowColor;
                    c7.BackgroundColor = rowColor;

                    c1.Padding = 8;
                    c2.Padding = 8;
                    c3.Padding = 8;
                    c4.Padding = 8;
                    c5.Padding = 8;
                    c6.Padding = 8;
                    c7.Padding = 8;

                    c1.HorizontalAlignment = Element.ALIGN_CENTER;
                    c5.HorizontalAlignment = Element.ALIGN_CENTER;
                    c6.HorizontalAlignment = Element.ALIGN_RIGHT;
                    c7.HorizontalAlignment = Element.ALIGN_RIGHT;

                    table.AddCell(c1);
                    table.AddCell(c2);
                    table.AddCell(c3);
                    table.AddCell(c4);
                    table.AddCell(c5);
                    table.AddCell(c6);
                    table.AddCell(c7);

                    index++;
                }

                doc.Add(table);

                doc.Add(new Paragraph(" "));
                doc.Add(new Paragraph(" "));

                // =====================================================
                // TOTAL TABLE
                // =====================================================

                PdfPTable totalTable = new PdfPTable(2);

                totalTable.WidthPercentage = 40;

                totalTable.HorizontalAlignment = Element.ALIGN_RIGHT;

                totalTable.SetWidths(new float[] { 50, 50 });

                // TOTAL QTY

                PdfPCell tq1 = new PdfPCell(
                    new Phrase("Total Quantity", boldFont)
                );

                tq1.Padding = 10;

                tq1.BackgroundColor = light;

                PdfPCell tq2 = new PdfPCell(
                    new Phrase(totalQty.ToString(), normalFont)
                );

                tq2.Padding = 10;

                tq2.HorizontalAlignment = Element.ALIGN_RIGHT;

                // GRAND TOTAL

                PdfPCell gt1 = new PdfPCell(
                    new Phrase("Grand Total", whiteFont)
                );

                gt1.Padding = 12;

                gt1.BackgroundColor = primary;

                PdfPCell gt2 = new PdfPCell(
                    new Phrase("₹ " + totalAmount.ToString("0.00"), whiteFont)
                );

                gt2.Padding = 12;

                gt2.HorizontalAlignment = Element.ALIGN_RIGHT;

                gt2.BackgroundColor = primary;

                totalTable.AddCell(tq1);
                totalTable.AddCell(tq2);

                totalTable.AddCell(gt1);
                totalTable.AddCell(gt2);

                doc.Add(totalTable);

                doc.Add(new Paragraph(" "));
                doc.Add(new Paragraph(" "));
                doc.Add(new Paragraph(" "));

                // =====================================================
                // FOOTER
                // =====================================================

                PdfPTable footerTable = new PdfPTable(1);

                footerTable.WidthPercentage = 100;

                PdfPCell footerCell = new PdfPCell();

                footerCell.Border = 0;

                footerCell.PaddingTop = 20;

                footerCell.HorizontalAlignment = Element.ALIGN_CENTER;

                Paragraph footer = new Paragraph(
                    "Thank you for your business!\n" +
                    "This is a computer generated invoice.\n" +
                    "Powered By Smart Stock ERP",
                    FontFactory.GetFont(
                        FontFactory.HELVETICA_OBLIQUE,
                        10,
                        BaseColor.GRAY
                    )
                );

                footer.Alignment = Element.ALIGN_CENTER;

                footerCell.AddElement(footer);

                footerTable.AddCell(footerCell);

                doc.Add(footerTable);
            }

            doc.Close();

            return File(
                ms.ToArray(),
                "application/pdf",
                $"Invoice_{id}.pdf"
            );
        }

        [HttpGet]
        public IActionResult GetProfit()
        {
            int companyId =
                int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
        SELECT
        ISNULL(SUM(si.LineTotal),0) AS Sales,

        ISNULL(SUM(p.CostPrice * si.Qty),0) AS Cost

        FROM SaleItems si
        JOIN Products p
        ON si.ProductId = p.ProductId

        WHERE si.CompanyId=@cid", con);

                cmd.Parameters.AddWithValue("@cid", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                decimal sales = 0;
                decimal cost = 0;

                if (dr.Read())
                {
                    sales = Convert.ToDecimal(dr["Sales"]);
                    cost = Convert.ToDecimal(dr["Cost"]);
                }

                decimal profit = sales - cost;

                return Json(new
                {
                    sales,
                    cost,
                    profit
                });
            }
        }

        public IActionResult InvoicePrint(int id)
        {
            return View(id);
        }

        private string GetAdminEmail(int companyId)
        {
            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT TOP 1 Email 
            FROM Users 
            WHERE CompanyId=@cid AND Role='Admin'", con);

                cmd.Parameters.AddWithValue("@cid", companyId);

                return cmd.ExecuteScalar()?.ToString();
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReturnSale(string invoiceNo, int productId, int qty, string reason)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();
                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    // GET SALE + CUSTOMER
                    SqlCommand cmd = new SqlCommand(@"
SELECT 
    s.SaleId,
    s.InvoiceNo,
    c.Email,
    c.CustomerName,
    si.Qty,
    p.ProductName
FROM Sales s
JOIN SaleItems si ON s.SaleId = si.SaleId
JOIN Customers c ON c.CustomerId = s.CustomerId
JOIN Products p ON p.ProductId = si.ProductId
WHERE s.InvoiceNo=@InvoiceNo
AND si.ProductId=@ProductId
AND s.CompanyId=@CompanyId", con, tx);

                    cmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);
                    cmd.Parameters.AddWithValue("@ProductId", productId);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);

                    SqlDataReader dr = cmd.ExecuteReader();

                    string email = "";
                    string name = "";
                    string productName = "";
                    string invoice = "";
                    int soldQty = 0;
                    int saleId = 0;

                    if (dr.Read())
                    {
                        saleId = Convert.ToInt32(dr["SaleId"]);
                        invoice = dr["InvoiceNo"].ToString();
                        email = dr["Email"].ToString();
                        name = dr["CustomerName"].ToString();
                        soldQty = Convert.ToInt32(dr["Qty"]);
                        productName = dr["ProductName"].ToString();
                    }

                    dr.Close();

                    if (qty > soldQty)
                        return Json(new { success = false, message = "Return qty greater than sold qty" });

                    // INSERT RETURN
                    SqlCommand ins = new SqlCommand(@"
INSERT INTO SalesReturn
(SaleId, ProductId, Qty, Reason, CompanyId, InvoiceNo)
VALUES
(@SaleId, @ProductId, @Qty, @Reason, @CompanyId, @InvoiceNo)", con, tx);

                    ins.Parameters.AddWithValue("@SaleId", saleId);
                    ins.Parameters.AddWithValue("@InvoiceNo", invoice);
                    ins.Parameters.AddWithValue("@ProductId", productId);
                    ins.Parameters.AddWithValue("@Qty", qty);
                    ins.Parameters.AddWithValue("@Reason", reason);
                    ins.Parameters.AddWithValue("@CompanyId", companyId);

                    ins.ExecuteNonQuery();

                    // STOCK BACK
                    SqlCommand stock = new SqlCommand(@"
                UPDATE Products
                SET StockQty = StockQty + @Qty
                WHERE ProductId=@ProductId AND CompanyId=@CompanyId", con, tx);

                    stock.Parameters.AddWithValue("@Qty", qty);
                    stock.Parameters.AddWithValue("@ProductId", productId);
                    stock.Parameters.AddWithValue("@CompanyId", companyId);

                    stock.ExecuteNonQuery();

                    tx.Commit();

                    // EMAIL CUSTOMER
                    if (!string.IsNullOrEmpty(email))
                    {
                        string body = $@"
<div style='margin:0;padding:0;background:#f4f6fb;font-family:Arial,sans-serif;'>

<table width='100%' cellpadding='0' cellspacing='0'>
<tr>
<td align='center' style='padding:40px 15px;'>

<table width='650' cellpadding='0' cellspacing='0'
       style='background:#ffffff;
              border-radius:18px;
              overflow:hidden;
              box-shadow:0 8px 25px rgba(0,0,0,0.08);'>

<!-- HEADER -->
<tr>
<td style='background:linear-gradient(135deg,#98b5dd,#6b7fb2);
           padding:35px;
           text-align:center;'>

    <h1 style='margin:0;color:white;font-size:30px;'>
        SMART STOCK ERP
    </h1>

    <p style='margin-top:10px;color:#eef4ff;font-size:15px;'>
        Sales Return Confirmation
    </p>

</td>
</tr>

<!-- BODY -->
<tr>
<td style='padding:40px;'>

<h2 style='margin-top:0;color:#1f3c88;font-size:22px;'>
    Hello {name},
</h2>

<p style='font-size:15px;color:#444;line-height:24px;'>
    Your return request has been successfully processed.
</p>

<!-- RETURN DETAILS -->
<table width='100%' cellpadding='10' cellspacing='0'
       style='margin-top:20px;
              border:1px solid #e6ecf5;
              border-radius:12px;
              background:#f9fbff;'>

<tr style='background:#eef4ff;'>
<td><b>Invoice No</b></td>
<td>{invoiceNo}</td>
</tr>

<tr>
<td><b>Product</b></td>
<td>{productName}</td>
</tr>

<tr style='background:#eef4ff;'>
<td><b>Returned Qty</b></td>
<td>{qty}</td>
</tr>

<tr>
<td><b>Reason</b></td>
<td>{reason}</td>
</tr>

<tr style='background:#eef4ff;'>
<td><b>Date</b></td>
<td>{DateTime.Now:dd MMM yyyy hh:mm tt}</td>
</tr>

</table>

<!-- NOTE -->
<div style='margin-top:25px;
            padding:15px;
            background:#f4f8ff;
            border-left:5px solid #6b7fb2;
            border-radius:8px;'>

<p style='margin:0;color:#444;font-size:14px;'>
Thank you for your return. If you have any questions, contact support.
</p>

</div>

</td>
</tr>

<!-- FOOTER -->
<tr>
<td style='background:#f7f9fc;
           padding:25px;
           text-align:center;
           border-top:1px solid #eaeef5;'>

<h3 style='margin:0;color:#1f3c88;'>Smart Stock ERP Pvt Ltd</h3>

<p style='margin:8px 0;color:#666;font-size:13px;'>
Indore, Madhya Pradesh
</p>

<p style='margin:5px 0;color:#666;font-size:13px;'>
📞 +91 8103452248 | ✉ support@smartstockerp.com
</p>

<p style='font-size:11px;color:#999;margin-top:15px;'>
Automated Email - Smart Stock ERP
</p>

</td>
</tr>

</table>

</td>
</tr>
</table>

</div>";
                        await _emailService.SendEmailAsync(
                            email,
                            name,
                            "Return Confirmation",
                            body
                        );
                    }

                    return Json(new { success = true, message = "Return processed" });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { success = false, message = ex.Message });
                }
            }
        }

        public IActionResult GetReturns()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"
        SELECT
            SR.ReturnId,
            SR.InvoiceNo,
            SR.ProductId,
            P.ProductName,
            SR.Qty,
            SR.Reason,
            FORMAT(SR.ReturnDate,'dd MMM yyyy') AS ReturnDate
        FROM SalesReturn SR
        INNER JOIN Products P
            ON P.ProductId = SR.ProductId
        WHERE SR.CompanyId=@CompanyId
        ORDER BY SR.ReturnId DESC";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                List<object> list = new List<object>();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        returnId = dr["ReturnId"],
                        invoiceNo = dr["InvoiceNo"],
                        productId = dr["ProductId"],
                        productName = dr["ProductName"],
                        qty = dr["Qty"],
                        reason = dr["Reason"],
                        date = dr["ReturnDate"]
                    });
                }

                return Json(list);
            }
        }

        public IActionResult Return()
        {
            return View();

        }
        [HttpPost]
        public IActionResult UpdateReturn(int returnId, int qty, string reason)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    // OLD QTY
                    SqlCommand oldCmd = new SqlCommand(@"
            SELECT ProductId, Qty
            FROM SalesReturn
            WHERE ReturnId=@ReturnId
            AND CompanyId=@CompanyId", con, tx);

                    oldCmd.Parameters.AddWithValue("@ReturnId", returnId);
                    oldCmd.Parameters.AddWithValue("@CompanyId", companyId);

                    SqlDataReader dr = oldCmd.ExecuteReader();

                    int oldQty = 0;
                    int productId = 0;

                    if (dr.Read())
                    {
                        oldQty = Convert.ToInt32(dr["Qty"]);
                        productId = Convert.ToInt32(dr["ProductId"]);
                    }

                    dr.Close();

                    int difference = qty - oldQty;

                    // UPDATE RETURN
                    SqlCommand update = new SqlCommand(@"
            UPDATE SalesReturn
            SET Qty=@Qty,
                Reason=@Reason
            WHERE ReturnId=@ReturnId
            AND CompanyId=@CompanyId", con, tx);

                    update.Parameters.AddWithValue("@Qty", qty);
                    update.Parameters.AddWithValue("@Reason", reason);
                    update.Parameters.AddWithValue("@ReturnId", returnId);
                    update.Parameters.AddWithValue("@CompanyId", companyId);

                    update.ExecuteNonQuery();

                    // UPDATE STOCK
                    SqlCommand stock = new SqlCommand(@"
            UPDATE Products
            SET StockQty = StockQty + @Diff
            WHERE ProductId=@ProductId
            AND CompanyId=@CompanyId", con, tx);

                    stock.Parameters.AddWithValue("@Diff", difference);
                    stock.Parameters.AddWithValue("@ProductId", productId);
                    stock.Parameters.AddWithValue("@CompanyId", companyId);

                    stock.ExecuteNonQuery();

                    tx.Commit();

                    return Json(new
                    {
                        success = true,
                        message = "Updated Successfully"
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();

                    return Json(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }
            }
        }

        [HttpPost]
        public IActionResult DeleteReturn(int returnId)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    int qty = 0;
                    int productId = 0;

                    SqlCommand get = new SqlCommand(@"
            SELECT ProductId, Qty
            FROM SalesReturn
            WHERE ReturnId=@ReturnId
            AND CompanyId=@CompanyId", con, tx);

                    get.Parameters.AddWithValue("@ReturnId", returnId);
                    get.Parameters.AddWithValue("@CompanyId", companyId);

                    SqlDataReader dr = get.ExecuteReader();

                    if (dr.Read())
                    {
                        qty = Convert.ToInt32(dr["Qty"]);
                        productId = Convert.ToInt32(dr["ProductId"]);
                    }

                    dr.Close();

                    // DELETE
                    SqlCommand del = new SqlCommand(@"
            DELETE FROM SalesReturn
            WHERE ReturnId=@ReturnId
            AND CompanyId=@CompanyId", con, tx);

                    del.Parameters.AddWithValue("@ReturnId", returnId);
                    del.Parameters.AddWithValue("@CompanyId", companyId);

                    del.ExecuteNonQuery();

                    // STOCK REVERSE
                    SqlCommand stock = new SqlCommand(@"
            UPDATE Products
            SET StockQty = StockQty - @Qty
            WHERE ProductId=@ProductId
            AND CompanyId=@CompanyId", con, tx);

                    stock.Parameters.AddWithValue("@Qty", qty);
                    stock.Parameters.AddWithValue("@ProductId", productId);
                    stock.Parameters.AddWithValue("@CompanyId", companyId);

                    stock.ExecuteNonQuery();

                    tx.Commit();

                    return Json(new
                    {
                        success = true
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();

                    return Json(new
                    {
                        success = false,
                        message = ex.Message
                    });
                }
            }
        }

        [HttpGet]
        public IActionResult GetInvoiceProducts(string invoiceNo)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"
        SELECT 
            SI.ProductId,
            P.ProductName,
            SI.Qty
        FROM Sales S
        INNER JOIN SaleItems SI ON S.SaleId = SI.SaleId
        INNER JOIN Products P ON P.ProductId = SI.ProductId
        WHERE S.InvoiceNo=@InvoiceNo
        AND S.CompanyId=@CompanyId";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                List<object> list = new List<object>();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        productId = dr["ProductId"],
                        productName = dr["ProductName"].ToString(),
                        qty = dr["Qty"]
                    });
                }

                return Json(list);
            }
        }
    }
}
