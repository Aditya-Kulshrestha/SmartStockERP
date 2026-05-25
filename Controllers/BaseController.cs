using Microsoft.AspNetCore.Mvc;

namespace SmartStockERP.Controllers
{
    public class BaseController : Controller
    {
        protected int CompanyId
        {
            get
            {
                var cid = HttpContext.Session.GetString("CompanyId");
                if (string.IsNullOrEmpty(cid))
                    throw new Exception("Session expired");
                return int.Parse(cid);
            }
        }

        protected string Role =>
            HttpContext.Session.GetString("Role") ?? "";

        protected string UserId =>
            HttpContext.Session.GetString("UserId") ?? "";
    }
}