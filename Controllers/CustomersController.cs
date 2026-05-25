using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartStockERP.Filters;

namespace SmartStockERP.Controllers
{
    [AuthCheck]
    public class CustomersController : Controller
    {
        string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("CompanyId") == null)
                return RedirectToAction("Index", "Login");

            return View();
        }

        [HttpPost]
        public IActionResult Add(string name, string phone, string email)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                string query = @"
                INSERT INTO Customers (CompanyId, CustomerName, Phone, Email)
                VALUES (@CompanyId, @Name, @Phone, @Email)";

                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@Phone", phone);
                cmd.Parameters.AddWithValue("@Email", email);

                con.Open();
                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            List<object> list = new List<object>();

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
                SELECT * FROM Customers
                WHERE CompanyId=@CompanyId
                ORDER BY CustomerId DESC", con);

                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();
                SqlDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        customerId = dr["CustomerId"],
                        name = dr["CustomerName"],
                        phone = dr["Phone"],
                        email = dr["Email"]
                    });
                }
            }

            return Json(list);
        }


        [HttpGet]
        public IActionResult GetById(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        SELECT * FROM Customers
        WHERE CustomerId=@Id
        AND CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                SqlDataReader dr = cmd.ExecuteReader();

                if (dr.Read())
                {
                    return Json(new
                    {
                        customerId = dr["CustomerId"],
                        name = dr["CustomerName"],
                        phone = dr["Phone"],
                        email = dr["Email"]
                    });
                }
            }

            return NotFound();
        }

        [HttpPost]
        public IActionResult Update(int id, string name, string phone, string email)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        UPDATE Customers
        SET CustomerName=@Name,
            Phone=@Phone,
            Email=@Email
        WHERE CustomerId=@Id
        AND CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@Phone", phone);
                cmd.Parameters.AddWithValue("@Email", email);

                con.Open();

                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

        [HttpPost]
        public IActionResult Delete(int id)
        {
            int companyId = int.Parse(HttpContext.Session.GetString("CompanyId"));

            using (SqlConnection con = new SqlConnection(connStr))
            {
                SqlCommand cmd = new SqlCommand(@"
        DELETE FROM Customers
        WHERE CustomerId=@Id
        AND CompanyId=@CompanyId", con);

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

                con.Open();

                cmd.ExecuteNonQuery();
            }

            return Ok();
        }

    }
}