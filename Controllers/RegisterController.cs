//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Data.SqlClient;

//namespace SmartStockERP.Controllers
//{
//    public class RegisterController : Controller
//    {
//        private readonly IConfiguration _configuration;

//        public RegisterController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        public IActionResult Index()
//        {
//            return View();
//        }

//        [HttpGet]
//        public IActionResult GetAllCompanies()
//        {
//            List<object> list = new List<object>();

//            string connStr = _configuration.GetConnectionString("DefaultConnection");

//            using (SqlConnection con = new SqlConnection(connStr))
//            {
//                con.Open();

//                SqlCommand cmd = new SqlCommand(@"
//            SELECT CompanyId, CompanyName
//            FROM Companies
//        ", con);

//                SqlDataReader dr = cmd.ExecuteReader();

//                while (dr.Read())
//                {
//                    list.Add(new
//                    {
//                        companyId = dr["CompanyId"],
//                        companyName = dr["CompanyName"]
//                    });
//                }
//            }

//            return Json(list);
//        }

//        [HttpPost]
//        public IActionResult Register(string companyId, string companyName, string ownerName, string email, string phone, string password, string role)
//        {
//            string connStr = _configuration.GetConnectionString("DefaultConnection");

//            using (SqlConnection con = new SqlConnection(connStr))
//            {
//                con.Open();
//                SqlTransaction tx = con.BeginTransaction();

//                try
//                {
//                    int finalCompanyId;

//                    // CASE 1: existing company
//                    if (!string.IsNullOrEmpty(companyId))
//                    {
//                        finalCompanyId = int.Parse(companyId);
//                    }
//                    else
//                    {
//                        // prevent duplicate company creation
//                        SqlCommand checkCompany = new SqlCommand(@"
//                    SELECT COUNT(*) FROM Companies WHERE CompanyName=@Name", con, tx);

//                        checkCompany.Parameters.AddWithValue("@Name", companyName);

//                        int existsCompany = (int)checkCompany.ExecuteScalar();

//                        if (existsCompany > 0)
//                        {
//                            tx.Rollback();
//                            return BadRequest("Company already exists");
//                        }

//                        SqlCommand ccmd = new SqlCommand(@"
//                    INSERT INTO Companies (CompanyName, Email, Phone)
//                    OUTPUT INSERTED.CompanyId
//                    VALUES (@Name,@Email,@Phone)", con, tx);

//                        ccmd.Parameters.AddWithValue("@Name", companyName);
//                        ccmd.Parameters.AddWithValue("@Email", email);
//                        ccmd.Parameters.AddWithValue("@Phone", phone);

//                        finalCompanyId = (int)ccmd.ExecuteScalar();
//                    }

//                    // USER DUPLICATE CHECK (FIXED)
//                    SqlCommand checkUser = new SqlCommand(@"
//                SELECT COUNT(*) FROM Users 
//                WHERE Email=@Email AND CompanyId=@CompanyId", con, tx);

//                    checkUser.Parameters.AddWithValue("@Email", email);
//                    checkUser.Parameters.AddWithValue("@CompanyId", finalCompanyId);

//                    int userExists = (int)checkUser.ExecuteScalar();

//                    if (userExists > 0)
//                    {
//                        tx.Rollback();
//                        return BadRequest("User already exists in this company");
//                    }

//                    SqlCommand ucmd = new SqlCommand(@"
//                INSERT INTO Users (CompanyId, Name, Email, Password, Role)
//                VALUES (@CompanyId,@Name,@Email,@Password,@Role)", con, tx);

//                    ucmd.Parameters.AddWithValue("@CompanyId", finalCompanyId);
//                    ucmd.Parameters.AddWithValue("@Name", ownerName);
//                    ucmd.Parameters.AddWithValue("@Email", email);
//                    ucmd.Parameters.AddWithValue("@Password", password);
//                    ucmd.Parameters.AddWithValue("@Role", role);

//                    ucmd.ExecuteNonQuery();

//                    tx.Commit();

//                    return Ok();
//                }
//                catch
//                {
//                    tx.Rollback();
//                    return BadRequest("Registration failed");
//                }
//            }
//        }
//    }
//}


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
            string connStr = _config.GetConnectionString("DefaultConnection");

            List<object> list = new();

            using (var con = new NpgsqlConnection(connStr))
            {
                con.Open();

                var cmd = new NpgsqlCommand(@"
    SELECT company_id AS companyId,
           company_name AS companyName
    FROM companies
", con);

                var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        companyId = dr["companyId"],
                        companyName = dr["companyName"]
                    });
                }
            }

            return Json(list);
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

            using (var con = new NpgsqlConnection(connStr))
            {
                con.Open();
                using var tx = con.BeginTransaction();

                try
                {
                    int finalCompanyId;

                    // CASE 1: existing company
                    if (companyId.HasValue)
                    {
                        finalCompanyId = companyId.Value;
                    }
                    else
                    {
                        var checkCompany = new NpgsqlCommand(@"
                            SELECT COUNT(*) FROM companies
                            WHERE company_name = @Name
                        ", con, tx);

                        checkCompany.Parameters.AddWithValue("@Name", companyName);

                        if ((long)checkCompany.ExecuteScalar() > 0)
                            return BadRequest("Company already exists");
                        var insertCompany = new NpgsqlCommand(@"
    INSERT INTO companies(company_name, email, phone)
    VALUES(@Name, @Email, @Phone)
    RETURNING company_id
", con);


                        insertCompany.Parameters.AddWithValue("@Name", companyName);
                        insertCompany.Parameters.AddWithValue("@Email",
    string.IsNullOrEmpty(email) ? DBNull.Value : email);

                        insertCompany.Parameters.AddWithValue("@Phone",
                            string.IsNullOrEmpty(phone) ? DBNull.Value : phone);

                        finalCompanyId = (int)insertCompany.ExecuteScalar();
                    }

                    // USER CHECK
                    var checkUser = new NpgsqlCommand(@"
                        SELECT COUNT(*) FROM users
                        WHERE email = @Email AND company_id = @CompanyId
                    ", con, tx);

                    checkUser.Parameters.AddWithValue("@Email", email);
                    checkUser.Parameters.AddWithValue("@CompanyId", finalCompanyId);

                    if ((long)checkUser.ExecuteScalar() > 0)
                        return BadRequest("User already exists in this company");

                    // INSERT USER
                    var insertUser = new NpgsqlCommand(@"
                        INSERT INTO users(company_id, name, email, password, role)
                        VALUES(@CompanyId, @Name, @Email, @Password, @Role)
                    ", con, tx);

                    insertUser.Parameters.AddWithValue("@CompanyId", finalCompanyId);
                    insertUser.Parameters.AddWithValue("@Name", ownerName);
                    insertUser.Parameters.AddWithValue("@Email", email);
                    insertUser.Parameters.AddWithValue("@Password", password);
                    insertUser.Parameters.AddWithValue("@Role", role);

                    insertUser.ExecuteNonQuery();

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
    }
}