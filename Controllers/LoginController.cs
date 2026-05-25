//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Data.SqlClient;

//public class LoginController : Controller
//{
//    string connStr = "Server=DESKTOP-OICVI98\\SQLEXPRESS;Database=SmartStockERP;Trusted_Connection=True;TrustServerCertificate=True;";

//    public IActionResult Index()
//    {
//        return View();
//    }

//    [HttpPost]
//    public IActionResult Login(string email, string password, int companyId)
//    {
//        using (SqlConnection con = new SqlConnection(connStr))
//        {
//            con.Open();

//            SqlCommand cmd = new SqlCommand(@"
//                SELECT u.*, c.CompanyName 
//                FROM Users u
//                JOIN Companies c ON u.CompanyId = c.CompanyId
//                WHERE u.Email=@Email 
//                AND u.Password=@Password 
//                AND u.CompanyId=@CompanyId", con);

//            cmd.Parameters.AddWithValue("@Email", email);
//            cmd.Parameters.AddWithValue("@Password", password);
//            cmd.Parameters.AddWithValue("@CompanyId", companyId);

//            SqlDataReader dr = cmd.ExecuteReader();

//            if (dr.Read())
//            {
//                HttpContext.Session.SetString("UserId", dr["UserId"].ToString());
//                HttpContext.Session.SetString("CompanyId", dr["CompanyId"].ToString());
//                HttpContext.Session.SetString("CompanyName", dr["CompanyName"].ToString());
//                HttpContext.Session.SetString("Name", dr["Name"].ToString());
//                HttpContext.Session.SetString("Role", dr["Role"].ToString());

//                return Json(new
//                {
//                    success = true,
//                    redirect = "/Dashboard/Index"
//                });
//            }
//        }

//        return Json(new { success = false, message = "Invalid login" });
//    }

//    [HttpPost]
//    public IActionResult GetCompanies(string email)
//    {
//        List<object> companies = new List<object>();

//        using (SqlConnection con = new SqlConnection(connStr))
//        {
//            con.Open();

//            SqlCommand cmd = new SqlCommand(@"
//                SELECT DISTINCT c.CompanyId, c.CompanyName
//                FROM Users u
//                INNER JOIN Companies c ON u.CompanyId = c.CompanyId
//                WHERE u.Email = @Email", con);

//            cmd.Parameters.AddWithValue("@Email", email);

//            SqlDataReader dr = cmd.ExecuteReader();

//            while (dr.Read())
//            {
//                companies.Add(new
//                {
//                    companyId = dr["CompanyId"],
//                    companyName = dr["CompanyName"]
//                });
//            }
//        }

//        return Json(companies);
//    }

//    public IActionResult Logout()
//    {
//        HttpContext.Session.Clear();
//        return RedirectToAction("Index");
//    }
//}


using Microsoft.AspNetCore.Mvc;
using Npgsql;

public class LoginController : Controller
{
    private readonly IConfiguration _config;

    public LoginController(IConfiguration config)
    {
        _config = config;
    }

    public IActionResult Index()
    {
        return View();
    }
    [HttpPost]
    public IActionResult Login(string email, string password, string companyId)
    {
        try
        {
            string connStr = _config.GetConnectionString("DefaultConnection");

            using var con = new NpgsqlConnection(connStr);
            con.Open();

            int cid = Convert.ToInt32(companyId);

            var cmd = new NpgsqlCommand(@"
            SELECT 
                u.user_id AS userId,
                u.company_id AS companyId,
                u.name AS name,
                u.role AS role,
                c.company_name AS companyName
            FROM users u
            JOIN companies c ON u.company_id = c.company_id
            WHERE u.email = @Email
            AND u.password = @Password
            AND u.company_id = @CompanyId
            LIMIT 1
        ", con);

            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Password", password);
            cmd.Parameters.AddWithValue("@CompanyId", cid);

            using var dr = cmd.ExecuteReader();

            if (dr.Read())
            {
                HttpContext.Session.SetString("UserId", dr["userId"].ToString());
                HttpContext.Session.SetString("CompanyId", dr["companyId"].ToString());
                HttpContext.Session.SetString("CompanyName", dr["companyName"].ToString());
                HttpContext.Session.SetString("Name", dr["name"].ToString());
                HttpContext.Session.SetString("Role", dr["role"].ToString());

                return Json(new
                {
                    success = true,
                    redirect = "/Dashboard/Index"
                });
            }

            return Json(new { success = false, message = "Invalid login" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost]
    public IActionResult GetCompanies(string email)
    {
        string connStr = _config.GetConnectionString("DefaultConnection");

        List<object> companies = new();

        using (var con = new NpgsqlConnection(connStr))
        {
            con.Open();

            var cmd = new NpgsqlCommand(@"
    SELECT DISTINCT c.company_id AS companyId,
                    c.company_name AS companyName
    FROM users u
    INNER JOIN companies c ON u.company_id = c.company_id
    WHERE u.email = @Email
", con);

            cmd.Parameters.AddWithValue("@Email", email);

            var dr = cmd.ExecuteReader();

            while (dr.Read())
            {
                companies.Add(new
                {
                    companyId = dr["companyId"],
                    companyName = dr["companyName"]
                });
            }
        }

        return Json(companies);
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }
}