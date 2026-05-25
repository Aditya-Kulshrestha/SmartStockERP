using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SmartStockERP.Controllers
{
    public class ReportsController : Controller
    {
        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                // ================= TOTAL SALES =================
                SqlCommand salesCmd = new SqlCommand(@"
            SELECT ISNULL(SUM(TotalAmount),0) 
            FROM Sales 
            WHERE CompanyId=@CompanyId", con);

                salesCmd.Parameters.AddWithValue("@CompanyId", companyId);
                ViewBag.TotalSales = salesCmd.ExecuteScalar();

                // ================= TOTAL INVOICES =================
                SqlCommand invoiceCmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM Sales 
            WHERE CompanyId=@CompanyId", con);

                invoiceCmd.Parameters.AddWithValue("@CompanyId", companyId);
                ViewBag.TotalInvoices = invoiceCmd.ExecuteScalar();

                // ================= TOP PRODUCT =================
                SqlCommand topCmd = new SqlCommand(@"
            SELECT TOP 1 p.ProductName
            FROM SaleItems si
            JOIN Products p ON si.ProductId = p.ProductId
            WHERE si.CompanyId=@CompanyId
            GROUP BY p.ProductName
            ORDER BY SUM(si.Qty) DESC", con);

                topCmd.Parameters.AddWithValue("@CompanyId", companyId);

                var result = topCmd.ExecuteScalar();
                ViewBag.TopProduct = result != null ? result.ToString() : "-";
            }

            return View();
        }
    }
}
