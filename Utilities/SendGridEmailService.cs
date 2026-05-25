using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Mail;

namespace SmartStockERP.Utilities
{
    public class SendGridEmailService
    {
        private readonly IConfiguration _config;

        public SendGridEmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<bool> SendEmailAsync(
            string toEmail,
            string toName,
            string subject,
            string htmlBody)
        {
            try
            {
                var apiKey = _config["SendGrid:ApiKey"];
                var fromEmail = _config["SendGrid:FromEmail"];
                var fromName = _config["SendGrid:FromName"];

                var client = new SendGridClient(apiKey);

                var from = new EmailAddress(fromEmail, fromName);
                var to = new EmailAddress(toEmail, toName);

                var msg = MailHelper.CreateSingleEmail(
                    from,
                    to,
                    subject,
                    "",
                    htmlBody
                );

                var response = await client.SendEmailAsync(msg);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}

