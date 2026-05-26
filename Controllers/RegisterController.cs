using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace SmartStockERP.Controllers
{
    public class RegisterController : Controller
    {
        private readonly IConfiguration _config;

        public RegisterController(IConfiguration config)
        {
            _config = config;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetAllCompanies()
        {
            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");

                var list = new List<object>();

                using var con = new NpgsqlConnection(connStr);
                con.Open();

                var cmd = new NpgsqlCommand(@"
                    SELECT company_id, company_name 
                    FROM companies 
                    ORDER BY company_id DESC
                ", con);

                using var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        companyId = dr["company_id"]?.ToString(),
                        companyName = dr["company_name"]?.ToString()
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost]
        public IActionResult Register(
            int? companyId,
            string companyName,
            string ownerName,
            string email,
            string phone,
            string password,
            string role)
        {
            string connStr = _config.GetConnectionString("DefaultConnection");

            using var con = new NpgsqlConnection(connStr);
            con.Open();
            using var tx = con.BeginTransaction();

            try
            {
                int finalCompanyId;

                // EXISTING COMPANY
                if (companyId.HasValue && companyId.Value > 0)
                {
                    finalCompanyId = companyId.Value;
                }
                else
                {
                    var checkCompany = new NpgsqlCommand(@"
                        SELECT COUNT(*) FROM companies WHERE company_name=@Name
                    ", con, tx);

                    checkCompany.Parameters.AddWithValue("@Name", companyName ?? "");

                    long exists = Convert.ToInt64(checkCompany.ExecuteScalar() ?? 0);

                    if (exists > 0)
                    {
                        tx.Rollback();
                        return BadRequest("Company already exists");
                    }

                    var insertCompany = new NpgsqlCommand(@"
                        INSERT INTO companies(company_name, email, phone)
                        VALUES(@Name, @Email, @Phone)
                        RETURNING company_id
                    ", con, tx);

                    insertCompany.Parameters.AddWithValue("@Name", companyName ?? "");
                    insertCompany.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
                    insertCompany.Parameters.AddWithValue("@Phone", (object?)phone ?? DBNull.Value);

                    finalCompanyId = Convert.ToInt32(insertCompany.ExecuteScalar());
                }

                // USER CHECK
                var checkUser = new NpgsqlCommand(@"
                    SELECT COUNT(*) FROM users 
                    WHERE email=@Email AND company_id=@CompanyId
                ", con, tx);

                checkUser.Parameters.AddWithValue("@Email", email ?? "");
                checkUser.Parameters.AddWithValue("@CompanyId", finalCompanyId);

                long userExists = Convert.ToInt64(checkUser.ExecuteScalar() ?? 0);

                if (userExists > 0)
                {
                    tx.Rollback();
                    return BadRequest("User already exists");
                }

                // INSERT USER
                var insertUser = new NpgsqlCommand(@"
                    INSERT INTO users(company_id, name, email, password, role)
                    VALUES(@CompanyId, @Name, @Email, @Password, @Role)
                ", con, tx);

                insertUser.Parameters.AddWithValue("@CompanyId", finalCompanyId);
                insertUser.Parameters.AddWithValue("@Name", ownerName ?? "");
                insertUser.Parameters.AddWithValue("@Email", email ?? "");
                insertUser.Parameters.AddWithValue("@Password", password ?? "");
                insertUser.Parameters.AddWithValue("@Role", role ?? "");

                insertUser.ExecuteNonQuery();

                tx.Commit();
                return Ok();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, ex.Message);
            }
        }
    }
}