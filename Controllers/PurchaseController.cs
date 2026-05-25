using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartStockERP.Utilities;
using System.ComponentModel.Design;

namespace SmartStockERP.Controllers
{
    public class PurchaseController : Controller
    {
        private readonly SendGridEmailService _emailService;

        public PurchaseController(SendGridEmailService emailService)
        {
            _emailService = emailService;
        }


        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SavePurchase(int productId, int qty, decimal purchasePrice)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            string invoiceNo = $"PUR-{DateTime.Now:yyyyMMdd}-{DateTime.Now.Ticks.ToString().Substring(10)}";
            decimal total = qty * purchasePrice;

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();
                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    SqlCommand cmd = new SqlCommand(@"
                        INSERT INTO Purchases
                        (ProductId, Qty, PurchasePrice, PurchaseDate, CompanyId, InvoiceNo, TotalAmount)
                        VALUES
                        (@ProductId, @Qty, @PurchasePrice, GETDATE(), @CompanyId, @InvoiceNo, @Total)", con, tx);

                    cmd.Parameters.AddWithValue("@ProductId", productId);
                    cmd.Parameters.AddWithValue("@Qty", qty);
                    cmd.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    cmd.Parameters.AddWithValue("@InvoiceNo", invoiceNo);
                    cmd.Parameters.AddWithValue("@Total", total);

                    cmd.ExecuteNonQuery();

                    SqlCommand stock = new SqlCommand(@"
                        UPDATE Products
                        SET StockQty = StockQty + @Qty
                        WHERE ProductId=@ProductId AND CompanyId=@CompanyId", con, tx);

                    stock.Parameters.AddWithValue("@Qty", qty);
                    stock.Parameters.AddWithValue("@ProductId", productId);
                    stock.Parameters.AddWithValue("@CompanyId", companyId);

                    stock.ExecuteNonQuery();
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

                    return Json(new { success = true, message = "Purchase saved" });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        public JsonResult GetProducts()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
                    SELECT ProductId, ProductName 
                    FROM Products 
                    WHERE CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        productId = dr["ProductId"],
                        productName = dr["ProductName"].ToString()
                    });
                }
            }

            return Json(list);
        }

        public IActionResult History()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetPurchases()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT 
                p.PurchaseId,
                p.InvoiceNo,
                pr.ProductName,
                p.Qty,
                p.PurchasePrice,
                p.TotalAmount,
                p.PurchaseDate
            FROM Purchases p
            INNER JOIN Products pr ON pr.ProductId = p.ProductId
            WHERE p.CompanyId=@CompanyId
            ORDER BY p.PurchaseId DESC", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        purchaseId = dr["PurchaseId"] == DBNull.Value ? 0 : Convert.ToInt32(dr["PurchaseId"]),
                        productName = dr["ProductName"] == DBNull.Value ? "" : dr["ProductName"].ToString(),
                        qty = dr["Qty"] == DBNull.Value ? 0 : Convert.ToInt32(dr["Qty"]),
                        price = dr["PurchasePrice"] == DBNull.Value ? 0 : Convert.ToDecimal(dr["PurchasePrice"]),
                        total = dr["TotalAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(dr["TotalAmount"]),
                        date = dr["PurchaseDate"] == DBNull.Value ? "": Convert.ToDateTime(dr["PurchaseDate"]).ToString("dd MMM yyyy"),
                        InvoiceNo = dr["InvoiceNo"] == DBNull.Value ? "" : dr["InvoiceNo"].ToString()
                    });
                }
            }

            return Json(list);
        }

        [HttpPost]
        public IActionResult DeletePurchase(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            DELETE FROM Purchases 
            WHERE PurchaseId=@Id AND CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                int rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                    return Json(new { success = true });
                else
                    return Json(new { success = false, message = "Not found" });
            }
        }

        public JsonResult GetPurchaseEdit(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
                    SELECT 
                        p.PurchaseId,
 p.ProductId,
                        p.InvoiceNo,
                        pr.ProductName,
                        p.Qty,
                        p.PurchasePrice,
                        p.TotalAmount,
                        p.PurchaseDate
                    FROM Purchases p
                    INNER JOIN Products pr ON pr.ProductId = p.ProductId
                    WHERE p.PurchaseId=@Id AND p.CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                if (dr.Read())
                {
                    return Json(new
                    {
                        purchaseId = dr["PurchaseId"],
                        productId = dr["ProductId"],
                        invoiceNo = dr["InvoiceNo"],
                        productName = dr["ProductName"].ToString(),
                        qty = dr["Qty"],
                        price = dr["PurchasePrice"],
                        total = dr["TotalAmount"],
                        date = Convert.ToDateTime(dr["PurchaseDate"]).ToString("dd MMM yyyy")
                    });
                }
            }

            return Json(null);
        }

        [HttpPost]
        public IActionResult UpdatePurchase(int id, int productId, int qty, decimal purchasePrice)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();
                SqlTransaction tx = con.BeginTransaction();

                try
                {
                    // old qty fetch
                    SqlCommand oldCmd = new SqlCommand(@"
                        SELECT Qty, ProductId 
                        FROM Purchases 
                        WHERE PurchaseId=@Id AND CompanyId=@CompanyId", con, tx);

                    oldCmd.Parameters.AddWithValue("@Id", id);
                    oldCmd.Parameters.AddWithValue("@CompanyId", companyId);

                    SqlDataReader dr = oldCmd.ExecuteReader();

                    int oldQty = 0;
                    int oldProductId = 0;

                    if (dr.Read())
                    {
                        oldQty = Convert.ToInt32(dr["Qty"]);
                        oldProductId = Convert.ToInt32(dr["ProductId"]);
                    }
                    dr.Close();

                    // revert old stock
                    SqlCommand revert = new SqlCommand(@"
                        UPDATE Products 
                        SET StockQty = StockQty - @OldQty 
                        WHERE ProductId=@OldProductId AND CompanyId=@CompanyId", con, tx);

                    revert.Parameters.AddWithValue("@OldQty", oldQty);
                    revert.Parameters.AddWithValue("@OldProductId", oldProductId);
                    revert.Parameters.AddWithValue("@CompanyId", companyId);
                    revert.ExecuteNonQuery();

                    // update purchase
                    SqlCommand update = new SqlCommand(@"
                        UPDATE Purchases
                        SET ProductId=@ProductId,
                            Qty=@Qty,
                            PurchasePrice=@PurchasePrice
                        WHERE PurchaseId=@Id AND CompanyId=@CompanyId", con, tx);

                    update.Parameters.AddWithValue("@ProductId", productId);
                    update.Parameters.AddWithValue("@Qty", qty);
                    update.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
                    update.Parameters.AddWithValue("@Id", id);
                    update.Parameters.AddWithValue("@CompanyId", companyId);

                    update.ExecuteNonQuery();

                    // add new stock
                    SqlCommand stock = new SqlCommand(@"
                        UPDATE Products 
                        SET StockQty = StockQty + @Qty 
                        WHERE ProductId=@ProductId AND CompanyId=@CompanyId", con, tx);

                    stock.Parameters.AddWithValue("@Qty", qty);
                    stock.Parameters.AddWithValue("@ProductId", productId);
                    stock.Parameters.AddWithValue("@CompanyId", companyId);

                    stock.ExecuteNonQuery();

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
        public IActionResult Invoice(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT 
                p.InvoiceNo,
                p.TotalAmount,
                p.PurchaseDate,
                c.CompanyName,
                c.Email,
                c.Phone
            FROM Purchases p
            JOIN Companies c ON p.CompanyId = c.CompanyId
            WHERE p.PurchaseId=@Id
            AND p.CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                string invoiceNo = "";
                string companyName = "";
                string email = "";
                string phone = "";
                decimal total = 0;
                DateTime date = DateTime.Now;

                if (dr.Read())
                {
                    invoiceNo = dr["InvoiceNo"].ToString();
                    companyName = dr["CompanyName"].ToString();
                    email = dr["Email"].ToString();
                    phone = dr["Phone"].ToString();
                    total = Convert.ToDecimal(dr["TotalAmount"]);
                    date = Convert.ToDateTime(dr["PurchaseDate"]);
                }

                dr.Close();

                SqlCommand itemCmd = new SqlCommand(@"
            SELECT 
                pr.ProductName,
                p.Qty,
                p.PurchasePrice,
                (p.Qty * p.PurchasePrice) AS LineTotal
            FROM Purchases p
            JOIN Products pr ON p.ProductId = pr.ProductId
            WHERE p.PurchaseId=@Id
            AND p.CompanyId=@CompanyId", con);

                itemCmd.Parameters.AddWithValue("@Id", id);
                itemCmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr2 = itemCmd.ExecuteReader();

                List<object> items = new();

                while (dr2.Read())
                {
                    items.Add(new
                    {
                        productName = dr2["ProductName"].ToString(),
                        qty = dr2["Qty"],
                        price = dr2["PurchasePrice"],
                        totalLine = dr2["LineTotal"]
                    });
                }

                return Json(new
                {
                    invoiceNo,
                    companyName,
                    email,
                    phone,
                    total,
                    date = date.ToString("dd MMM yyyy"),
                    items
                });
            }
        }

        public IActionResult InvoicePrint(int id)
        {
            return View(id);
        }
        public IActionResult InvoicePdf(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            MemoryStream ms = new MemoryStream();

            Document doc = new Document(PageSize.A4, 30, 30, 40, 40);
            PdfWriter.GetInstance(doc, ms);

            doc.Open();

            // ================= COLORS =================
            BaseColor primary = new BaseColor(107, 127, 178);
            BaseColor primaryDark = new BaseColor(79, 99, 150);
            BaseColor primaryLight = new BaseColor(152, 181, 221);

            Font titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 20, BaseColor.WHITE);
            Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.BLACK);
            Font boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.BLACK);
            Font whiteFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.WHITE);

            // ================= DATA =================
            string invoiceNo = "";
            string companyName = "";
            string phone = "";
            string email = "";
            decimal total = 0;
            DateTime date = DateTime.Now;

            List<dynamic> items = new List<dynamic>();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                // HEADER DATA
                SqlCommand cmd = new SqlCommand(@"
            SELECT 
                p.InvoiceNo,
                p.TotalAmount,
                p.PurchaseDate,
                c.CompanyName,
                c.Phone,
                c.Email
            FROM Purchases p
            JOIN Companies c ON p.CompanyId = c.CompanyId
            WHERE p.PurchaseId=@Id AND p.CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                if (dr.Read())
                {
                    invoiceNo = dr["InvoiceNo"].ToString();
                    companyName = dr["CompanyName"].ToString();
                    phone = dr["Phone"].ToString();
                    email = dr["Email"].ToString();
                    total = Convert.ToDecimal(dr["TotalAmount"]);
                    date = Convert.ToDateTime(dr["PurchaseDate"]);
                }

                dr.Close();

                // ITEMS
                SqlCommand itemCmd = new SqlCommand(@"
            SELECT 
                pr.ProductName,
                p.Qty,
                p.PurchasePrice,
                (p.Qty * p.PurchasePrice) AS LineTotal
            FROM Purchases p
            JOIN Products pr ON p.ProductId = pr.ProductId
            WHERE p.PurchaseId=@Id AND p.CompanyId=@CompanyId", con);

                itemCmd.Parameters.AddWithValue("@Id", id);
                itemCmd.Parameters.AddWithValue("@CompanyId", companyId);

                SqlDataReader dr2 = itemCmd.ExecuteReader();

                while (dr2.Read())
                {
                    items.Add(new
                    {
                        productName = dr2["ProductName"].ToString(),
                        qty = dr2["Qty"],
                        price = dr2["PurchasePrice"],
                        totalLine = dr2["LineTotal"]
                    });
                }

                dr2.Close();
            }

            // ================= HEADER =================
            PdfPTable header = new PdfPTable(2);
            header.WidthPercentage = 100;
            header.SetWidths(new float[] { 70, 30 });

            PdfPCell left = new PdfPCell(new Phrase("SMART STOCK ERP", titleFont));
            left.BackgroundColor = primaryLight;
            left.Padding = 20;
            left.Border = 0;

            PdfPCell right = new PdfPCell(new Phrase("PURCHASE INVOICE", titleFont));
            right.BackgroundColor = primaryDark;
            right.Padding = 20;
            right.Border = 0;
            right.HorizontalAlignment = Element.ALIGN_RIGHT;

            header.AddCell(left);
            header.AddCell(right);

            doc.Add(header);

            doc.Add(new Paragraph("\nCompany: " + companyName, normalFont));
            doc.Add(new Paragraph("Phone: " + phone + " | Email: " + email, normalFont));
            doc.Add(new Paragraph("Invoice No: " + invoiceNo, boldFont));
            doc.Add(new Paragraph("Date: " + date.ToString("dd MMM yyyy"), normalFont));

            doc.Add(new Paragraph("\n"));

            // ================= TABLE =================
            PdfPTable table = new PdfPTable(5);
            table.WidthPercentage = 100;

            string[] heads = { "Sr", "Product", "Qty", "Price", "Total" };

            foreach (var h in heads)
            {
                PdfPCell c = new PdfPCell(new Phrase(h, whiteFont));
                c.BackgroundColor = primaryDark;
                c.Padding = 8;
                c.HorizontalAlignment = Element.ALIGN_CENTER;
                table.AddCell(c);
            }

            int i = 1;

            foreach (var x in items)
            {
                table.AddCell(new Phrase(i++.ToString(), normalFont));
                table.AddCell(new Phrase(x.productName, normalFont));
                table.AddCell(new Phrase(x.qty.ToString(), normalFont));
                table.AddCell(new Phrase("₹ " + x.price.ToString("0.00"), normalFont));
                table.AddCell(new Phrase("₹ " + x.totalLine.ToString("0.00"), boldFont));
            }

            doc.Add(table);

            doc.Add(new Paragraph("\nGrand Total: ₹ " + total.ToString("0.00"), boldFont));

            doc.Close();

            return File(ms.ToArray(), "application/pdf", $"PurchaseInvoice_{id}.pdf");
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
    }
}
