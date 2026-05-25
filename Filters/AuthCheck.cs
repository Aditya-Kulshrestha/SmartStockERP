using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartStockERP.Filters
{
    public class AuthCheck : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session = context.HttpContext.Session.GetString("CompanyId");

            if (session == null)
            {
                context.Result = new RedirectResult("/Login/Index");
            }
        }
    }
}