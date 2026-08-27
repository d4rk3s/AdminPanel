namespace task4.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string confirmLink)
        {
            string apiKey = _configuration["BrevoApiKey"] ?? Environment.GetEnvironmentVariable("BREVO_API_KEY") ?? "";
            string senderEmail = "egorkon666@gmail.com";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

            var payload = new
            {
                sender = new { name = "The App Support", email = senderEmail },
                to = new[] { new { email = toEmail } },
                subject = "Registration Confirmation",
                htmlContent = $"<h3>Welcome!</h3><p>To activate your account, please click the link below:</p><a href='{confirmLink}'>{confirmLink}</a>"
            };

            var response = await httpClient.PostAsJsonAsync("https://api.brevo.com/v3/smtp/email", payload);

            if (!response.IsSuccessStatusCode)
            {
                string errorResponseBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Brevo API Error ({response.StatusCode}): {errorResponseBody}");
            }
        }
    }
}