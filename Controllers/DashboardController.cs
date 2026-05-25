using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartStockERP.Filters;
using System.ComponentModel.Design;

namespace SmartStockERP.Controllers
{
    [AuthCheck]
    public class DashboardController : BaseController
    {
        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            ViewBag.Role = HttpContext.Session.GetString("Role");
            ViewBag.CompanyId = CompanyId; // already from BaseController
            ViewBag.UserName = HttpContext.Session.GetString("Name");

            return View();
        }

        [HttpGet]
        public IActionResult GetStats()
        {
            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                // PRODUCTS
                SqlCommand p = new SqlCommand(
                    "SELECT COUNT(*) FROM Products WHERE CompanyId=@cid", con);
                p.Parameters.AddWithValue("@cid", CompanyId);

                int products = (int)p.ExecuteScalar();

                // SALES
                SqlCommand s = new SqlCommand(
                    "SELECT COUNT(*) FROM Sales WHERE CompanyId=@cid", con);
                s.Parameters.AddWithValue("@cid", CompanyId);

                int sales = (int)s.ExecuteScalar();

                // REVENUE
                SqlCommand r = new SqlCommand(
                    "SELECT ISNULL(SUM(TotalAmount),0) FROM Sales WHERE CompanyId=@cid", con);
                r.Parameters.AddWithValue("@cid", CompanyId);

                decimal revenue = Convert.ToDecimal(r.ExecuteScalar());

                // LOW STOCK
                SqlCommand l = new SqlCommand(
                    "SELECT COUNT(*) FROM Products WHERE CompanyId=@cid AND StockQty < 5", con);
                l.Parameters.AddWithValue("@cid", CompanyId);

                int lowStock = (int)l.ExecuteScalar();

                return Json(new { products, sales, revenue, lowStock });
            }
        }

        [HttpGet]
        public IActionResult MonthlySales()
        {
            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT 
                DATENAME(MONTH, SaleDate) AS MonthName,
                SUM(TotalAmount) AS Total
            FROM Sales
            WHERE CompanyId = @cid
            GROUP BY DATENAME(MONTH, SaleDate), MONTH(SaleDate)
            ORDER BY MONTH(SaleDate)", con);

                cmd.Parameters.AddWithValue("@cid", CompanyId);

                SqlDataReader dr = cmd.ExecuteReader();

                List<string> months = new List<string>();
                List<decimal> totals = new List<decimal>();

                while (dr.Read())
                {
                    months.Add(dr["MonthName"].ToString());
                    totals.Add(Convert.ToDecimal(dr["Total"]));
                }

                return Json(new { months, totals });
            }
        }

        [HttpGet]
        public IActionResult GetLowStockItems()
        {
            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT ProductName, StockQty, ReorderLevel
            FROM Products
            WHERE CompanyId=@cid
            AND StockQty <= ReorderLevel", con);

                cmd.Parameters.AddWithValue("@cid", CompanyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        productName = dr["ProductName"].ToString(),
                        stock = Convert.ToInt32(dr["StockQty"])
                    });
                }
            }

            return Json(list);
        }
    }
}
   