using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartStockERP.Filters;

namespace SmartStockERP.Controllers
{
    [AuthCheck]
    public class ProductController : BaseController
    {
        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            if (Role != "Admin")
                return RedirectToAction("Index", "Dashboard");

            return View();
        }

        [HttpPost]
        public IActionResult Add(string name, string sku, decimal costPrice, decimal price, int qty, string unit, string categoryName)
        {
            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"
            INSERT INTO Products
            (CompanyId, ProductName, SKU,CostPrice, Price, StockQty,Unit,CategoryName)
            VALUES (@CompanyId,@Name,@SKU,@CostPrice,@Price,@Qty,@Unit,@CategoryName)";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@CompanyId", CompanyId);
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@SKU", sku);
                cmd.Parameters.AddWithValue("@CostPrice", costPrice);
                cmd.Parameters.AddWithValue("@Price", price);
                cmd.Parameters.AddWithValue("@Qty", qty);
                cmd.Parameters.AddWithValue("@Unit", unit);
                cmd.Parameters.AddWithValue("@CategoryName", categoryName);
                con.Open();
                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        SELECT * FROM Products
        WHERE CompanyId=@CompanyId
        ORDER BY ProductId DESC", con);

                cmd.Parameters.AddWithValue("@CompanyId", CompanyId);

                con.Open();
                var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        productId = dr["ProductId"],
                        productName = dr["ProductName"],
                        sku = dr["SKU"],
                        price = dr["Price"],
                        stockQty = dr["StockQty"],
                        Unit = dr["Unit"],
                        CostPrice = dr["CostPrice"],
                        category = dr["CategoryName"]
                    });
                }
            }

            return Json(list);
        }

        [HttpPost]
        public IActionResult Delete(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"DELETE FROM Products 
                         WHERE ProductId=@Id AND CompanyId=@CompanyId";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();
                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

        [HttpGet]
        public IActionResult GetById(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"SELECT * FROM Products 
                         WHERE ProductId=@Id AND CompanyId=@CompanyId";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                if (dr.Read())
                {
                    return Json(new
                    {
                        ProductId = dr["ProductId"],
                        ProductName = dr["ProductName"],
                        SKU = dr["SKU"],
                        Price = dr["Price"],
                        StockQty = dr["StockQty"],
                        Unit = dr["Unit"],
                        CostPrice = dr["CostPrice"],
                        categoryName = dr["CategoryName"]
                    });
                }
            }

            return NotFound();
        }

        [HttpPost]
        public IActionResult Update(int id, string name, string sku, decimal price, int qty, string unit, string categoryName)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"UPDATE Products
                         SET ProductName=@Name,
                             SKU=@SKU,
                             Price=@Price,
                             StockQty=@Qty,
                             Unit=@Unit,
                             CategoryName=@CategoryName
                         WHERE ProductId=@Id AND CompanyId=@CompanyId";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@SKU", sku);
                cmd.Parameters.AddWithValue("@Price", price);
                cmd.Parameters.AddWithValue("@Qty", qty);
                cmd.Parameters.AddWithValue("@Unit", unit);
                cmd.Parameters.AddWithValue("@CategoryName", categoryName);

                con.Open();
                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

        [HttpGet]
        public IActionResult GetDropdown()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new List<object>();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        SELECT ProductId, ProductName, Price, StockQty
        FROM Products
        WHERE CompanyId=@CompanyId", con);

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
                        stock = dr["StockQty"]
                    });
                }
            }

            return Json(list);
        }

        [HttpGet]
        public IActionResult GetCategories()
        {
            List<object> list = new();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        SELECT CategoryName
        FROM Categories
        WHERE CompanyId=@CompanyId
        ORDER BY CategoryName", con);

                cmd.Parameters.AddWithValue("@CompanyId", CompanyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        categoryName = dr["CategoryName"]
                    });
                }
            }

            return Json(list);
        }

        [HttpPost]
        public IActionResult AddCategory(string categoryName)
        {
            using (SqlConnection con = new SqlConnection(connStr))
            {
                string checkQuery = @"
        SELECT COUNT(*)
        FROM Categories
        WHERE CategoryName=@CategoryName
        AND CompanyId=@CompanyId";

                SqlCommand checkCmd = new SqlCommand(checkQuery, con);

                checkCmd.Parameters.AddWithValue("@CategoryName", categoryName);

                checkCmd.Parameters.AddWithValue("@CompanyId", CompanyId);

                con.Open();

                int exists = (int)checkCmd.ExecuteScalar();

                if (exists > 0)
                {
                    return BadRequest("Category already exists");
                }

                string query = @"
        INSERT INTO Categories
        (CategoryName, CompanyId)
        VALUES
        (@CategoryName, @CompanyId)";

                SqlCommand cmd = new SqlCommand(query, con);

                cmd.Parameters.AddWithValue("@CategoryName", categoryName);

                cmd.Parameters.AddWithValue("@CompanyId", CompanyId);

                cmd.ExecuteNonQuery();
            }

            return Ok();
        }
    }
}
