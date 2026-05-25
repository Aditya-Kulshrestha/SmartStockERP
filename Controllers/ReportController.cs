using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SmartStockERP.Controllers
{
    public class ReportController : Controller
    {

        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";
        public IActionResult Inventory()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetInventory()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                con.Open();

                SqlCommand cmd = new SqlCommand(@"
            SELECT 
                ProductName,
                StockQty,
                Price,
                CostPrice,
                (StockQty * CostPrice) AS StockValue,
                Unit
            FROM Products
            WHERE CompanyId = @cid", con);

                cmd.Parameters.AddWithValue("@cid", companyId);

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        productName = dr["ProductName"].ToString(),
                        stock = Convert.ToInt32(dr["StockQty"]),
                        price = Convert.ToDecimal(dr["Price"]),
                        costPrice = Convert.ToDecimal(dr["CostPrice"]),
                        stockValue = Convert.ToDecimal(dr["StockValue"]),
                        unit = dr["Unit"].ToString(),
                    });
                }
            }

            return Json(list);
        }
    }
}
