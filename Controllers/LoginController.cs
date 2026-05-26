
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

            if (string.IsNullOrEmpty(companyId))
                return Json(new { success = false, message = "Company required" });

            int cid = 0;
            int.TryParse(companyId, out cid);

            using var con = new NpgsqlConnection(connStr);
            con.Open();

            var cmd = new NpgsqlCommand(@"
                SELECT u.user_id, u.company_id, u.name, u.role, c.company_name
                FROM users u
                JOIN companies c ON u.company_id = c.company_id
                WHERE u.email = @Email
                AND u.password = @Password
                AND u.company_id = @CompanyId
                LIMIT 1
            ", con);

            cmd.Parameters.AddWithValue("@Email", email ?? "");
            cmd.Parameters.AddWithValue("@Password", password ?? "");
            cmd.Parameters.AddWithValue("@CompanyId", cid);

            using var dr = cmd.ExecuteReader();

            if (dr.Read())
            {
                HttpContext.Session.SetString("UserId", dr["user_id"].ToString());
                HttpContext.Session.SetString("CompanyId", dr["company_id"].ToString());
                HttpContext.Session.SetString("CompanyName", dr["company_name"].ToString());
                HttpContext.Session.SetString("Name", dr["name"].ToString());
                HttpContext.Session.SetString("Role", dr["role"].ToString());

                return Json(new { success = true, redirect = "/Dashboard/Index" });
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
        try
        {
            string connStr = _config.GetConnectionString("DefaultConnection");

            List<object> companies = new();

            using var con = new NpgsqlConnection(connStr);
            con.Open();

            var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT c.company_id, c.company_name
            FROM users u
            JOIN companies c ON u.company_id = c.company_id
            WHERE u.email = @Email
        ", con);

            cmd.Parameters.AddWithValue("@Email", email);

            using var dr = cmd.ExecuteReader();

            while (dr.Read())
            {
                companies.Add(new
                {
                    companyId = dr["company_id"],
                    companyName = dr["company_name"]
                });
            }

            return Json(companies);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }
}