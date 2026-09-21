using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace ZimbabweTenderAPI.Services
{
    public interface IEmailService
    {
        Task<bool> SendTenderAssignmentEmailAsync(string toEmail, string toName, string tenderTitle, string tenderReference, string tenderUrl, string assignedBy, string instructions, DateTime? dueDate);
        Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
        Task<bool> SendApprovalRequestEmailAsync(string toEmail, string approverName, string tenderTitle, string tenderReference, string stage, string requestedBy);
        Task<bool> SendApprovalNotificationEmailAsync(string toEmail, string requesterName, string tenderTitle, string tenderReference, string stage, string status, string approverComments);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendTenderAssignmentEmailAsync(
            string toEmail,
            string toName,
            string tenderTitle,
            string tenderReference,
            string tenderUrl,
            string assignedBy,
            string instructions,
            DateTime? dueDate)
        {
            var subject = $"Tender Assignment: {tenderReference} - {tenderTitle}";

            var body = $@"
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: #007bff; color: white; padding: 20px; text-align: center; }}
                        .content {{ background-color: #f8f9fa; padding: 20px; margin-top: 20px; }}
                        .tender-info {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #007bff; }}
                        .button {{ display: inline-block; padding: 12px 24px; background-color: #007bff; color: white; text-decoration: none; border-radius: 5px; margin-top: 15px; }}
                        .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>New Tender Assignment</h2>
                        </div>
                        <div class='content'>
                            <p>Hello {toName},</p>
                            <p>You have been assigned to work on the following tender by <strong>{assignedBy}</strong>:</p>
                            
                            <div class='tender-info'>
                                <h3>{tenderTitle}</h3>
                                <p><strong>Reference:</strong> {tenderReference}</p>
                                {(dueDate.HasValue ? $"<p><strong>Due Date:</strong> {dueDate.Value:dddd, MMMM dd, yyyy}</p>" : "")}
                            </div>

                            {(!string.IsNullOrEmpty(instructions) ? $@"
                            <div class='tender-info'>
                                <h4>Instructions:</h4>
                                <p>{instructions}</p>
                            </div>" : "")}

                            <p>Click the button below to view the tender details:</p>
                            <a href='{tenderUrl}' class='button'>View Tender Details</a>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from Zimbabwe Tender API System</p>
                            <p>&copy; {DateTime.Now.Year} Axis Solutions. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>";

            return await SendEmailAsync(toEmail, subject, body, true);
        }

        public async Task<bool> SendApprovalRequestEmailAsync(
            string toEmail,
            string approverName,
            string tenderTitle,
            string tenderReference,
            string stage,
            string requestedBy)
        {
            var subject = $"Tender Approval Request: {tenderReference} - {stage}";

            var body = $@"
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: #28a745; color: white; padding: 20px; text-align: center; }}
                        .content {{ background-color: #f8f9fa; padding: 20px; margin-top: 20px; }}
                        .tender-info {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #28a745; }}
                        .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>Tender Approval Request</h2>
                        </div>
                        <div class='content'>
                            <p>Hello {approverName},</p>
                            <p>A tender requires your approval at the <strong>{stage}</strong> stage:</p>
                            
                            <div class='tender-info'>
                                <h3>{tenderTitle}</h3>
                                <p><strong>Reference:</strong> {tenderReference}</p>
                                <p><strong>Approval Stage:</strong> {stage}</p>
                                <p><strong>Requested By:</strong> {requestedBy}</p>
                            </div>

                            <p>Please log in to the system to review and approve this tender.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from Zimbabwe Tender API System</p>
                            <p>&copy; {DateTime.Now.Year} Axis Solutions. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>";

            return await SendEmailAsync(toEmail, subject, body, true);
        }

        public async Task<bool> SendApprovalNotificationEmailAsync(
            string toEmail,
            string requesterName,
            string tenderTitle,
            string tenderReference,
            string stage,
            string status,
            string approverComments)
        {
            var statusColor = status == "Approved" ? "#28a745" : (status == "Rejected" ? "#dc3545" : "#ffc107");
            var subject = $"Tender {status}: {tenderReference} - {stage}";

            var body = $@"
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: {statusColor}; color: white; padding: 20px; text-align: center; }}
                        .content {{ background-color: #f8f9fa; padding: 20px; margin-top: 20px; }}
                        .tender-info {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid {statusColor}; }}
                        .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>Tender {status}</h2>
                        </div>
                        <div class='content'>
                            <p>Hello {requesterName},</p>
                            <p>Your tender has been <strong>{status}</strong> at the <strong>{stage}</strong> stage:</p>
                            
                            <div class='tender-info'>
                                <h3>{tenderTitle}</h3>
                                <p><strong>Reference:</strong> {tenderReference}</p>
                                <p><strong>Stage:</strong> {stage}</p>
                                <p><strong>Status:</strong> {status}</p>
                                {(!string.IsNullOrEmpty(approverComments) ? $"<p><strong>Comments:</strong> {approverComments}</p>" : "")}
                            </div>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from Zimbabwe Tender API System</p>
                            <p>&copy; {DateTime.Now.Year} Axis Solutions. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>";

            return await SendEmailAsync(toEmail, subject, body, true);
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
        {
            try
            {
                var smtpHost = _configuration["EmailSettings:SmtpHost"];
                var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
                var fromEmail = _configuration["EmailSettings:FromEmail"];
                var fromName = _configuration["EmailSettings:FromName"];
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var enableSsl = bool.Parse(_configuration["EmailSettings:EnableSsl"] ?? "true");

                if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(fromEmail))
                {
                    _logger.LogWarning("Email settings not configured. Skipping email send.");
                    return false;
                }

                using (var client = new SmtpClient(smtpHost, smtpPort))
                {
                    client.EnableSsl = enableSsl;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(username, password);

                    var message = new MailMessage
                    {
                        From = new MailAddress(fromEmail, fromName),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = isHtml
                    };

                    message.To.Add(toEmail);

                    await client.SendMailAsync(message);
                    _logger.LogInformation("Email sent successfully to {Email}", toEmail);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}: {Message}", toEmail, ex.Message);
                return false;
            }
        }
    }
}
