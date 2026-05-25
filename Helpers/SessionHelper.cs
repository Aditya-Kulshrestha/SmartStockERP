namespace SmartStockERP.Helpers
{
    public static class SessionHelper
    {
        public static void Set(HttpContext context, string key, string value)
        {
            context.Session.SetString(key, value);
        }

        public static string Get(HttpContext context, string key)
        {
            return context.Session.GetString(key);
        }
    }
}
