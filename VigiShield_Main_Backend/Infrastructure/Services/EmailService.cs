using System.Net;
using System.Net.Mail;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Envía correo transaccional por SMTP (recuperación de contraseña).
///
/// No hace nada hasta estar configurado (Email:Username + Email:Password), igual
/// que WhatsApp y FCM, así que es seguro dejarlo apagado en desarrollo. Las
/// credenciales llegan por el EnvironmentFile del servidor (Email__Username,
/// Email__Password), NUNCA en appsettings versionado.
///
/// Con Gmail hay que usar una CONTRASEÑA DE APLICACIÓN (la del avatar no sirve
/// desde que Google retiró el acceso de apps menos seguras). Google la muestra en
/// cuatro grupos separados por espacios; aquí se quitan porque el servidor SMTP
/// espera los 16 caracteres seguidos — pegarla tal cual era un fallo de
/// autenticación difícil de diagnosticar.
/// </summary>
public class EmailService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<EmailService> _log;

    public EmailService(IConfiguration cfg, ILogger<EmailService> log)
    {
        _cfg = cfg;
        _log = log;
    }

    private string Host => _cfg["Email:SmtpHost"] ?? "smtp.gmail.com";
    private int Port => int.TryParse(_cfg["Email:SmtpPort"], out var p) ? p : 587;
    private string? Username => _cfg["Email:Username"];
    private string? Password => _cfg["Email:Password"]?.Replace(" ", "");
    private string FromAddress => _cfg["Email:FromAddress"] ?? Username ?? "";
    private string FromName => _cfg["Email:FromName"] ?? "VigiShield";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Envía un correo HTML. Devuelve false (sin lanzar) si falla, para
    /// que un problema de correo nunca tumbe la petición del usuario.</summary>
    public async Task<bool> SendAsync(string to, string subject, string htmlBody)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("Email no configurado — no se envía '{Subject}' a {To}", subject, Mask(to));
            return false;
        }

        try
        {
            using var client = new SmtpClient(Host, Port)
            {
                EnableSsl = true, // STARTTLS en el 587
                Credentials = new NetworkCredential(Username, Password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000,
            };
            using var msg = new MailMessage
            {
                From = new MailAddress(FromAddress, FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            msg.To.Add(to);

            await client.SendMailAsync(msg);
            _log.LogInformation("Correo '{Subject}' enviado a {To}", subject, Mask(to));
            return true;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Fallo al enviar el correo '{Subject}' a {To}", subject, Mask(to));
            return false;
        }
    }

    /// <summary>Plantilla del correo de recuperación de contraseña.</summary>
    public Task<bool> SendPasswordResetAsync(string to, string name, string resetUrl)
    {
        var html = $"""
            <div style="font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#0A0F1E;padding:32px">
              <div style="max-width:520px;margin:0 auto;background:#111827;border:1px solid #1F2937;border-radius:16px;padding:32px">
                <h1 style="color:#00C2FF;font-size:20px;margin:0 0 4px">VigiShield</h1>
                <p style="color:#9CA3AF;font-size:12px;margin:0 0 24px;letter-spacing:2px">SEGURIDAD INTELIGENTE</p>
                <p style="color:#E5E7EB;font-size:15px;line-height:1.6;margin:0 0 16px">
                  Hola{(string.IsNullOrWhiteSpace(name) ? "" : " " + WebUtility.HtmlEncode(name))}:
                </p>
                <p style="color:#E5E7EB;font-size:15px;line-height:1.6;margin:0 0 24px">
                  Recibimos una solicitud para restablecer la contraseña de tu cuenta.
                  Pulsa el botón para elegir una nueva. El enlace caduca en 24 horas.
                </p>
                <p style="margin:0 0 24px">
                  <a href="{resetUrl}" style="display:inline-block;background:#00C2FF;color:#0A0F1E;
                     text-decoration:none;font-weight:700;font-size:15px;padding:14px 28px;border-radius:12px">
                    Restablecer mi contraseña
                  </a>
                </p>
                <p style="color:#9CA3AF;font-size:13px;line-height:1.6;margin:0 0 8px">
                  Si el botón no funciona, copia esta dirección en tu navegador:
                </p>
                <p style="color:#00C2FF;font-size:12px;word-break:break-all;margin:0 0 24px">{resetUrl}</p>
                <p style="color:#6B7280;font-size:12px;line-height:1.6;margin:0;border-top:1px solid #1F2937;padding-top:16px">
                  Si no solicitaste este cambio, ignora este mensaje: tu contraseña actual seguirá funcionando.
                </p>
              </div>
            </div>
            """;
        return SendAsync(to, "Restablece tu contraseña de VigiShield", html);
    }

    /// <summary>Plantilla del correo de invitación a una vivienda.</summary>
    public Task<bool> SendInvitationAsync(string to, string quienInvita, string inviteUrl)
    {
        var de = string.IsNullOrWhiteSpace(quienInvita)
            ? "El residente principal"
            : WebUtility.HtmlEncode(quienInvita);
        var html = $"""
            <div style="font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#0A0F1E;padding:32px">
              <div style="max-width:520px;margin:0 auto;background:#111827;border:1px solid #1F2937;border-radius:16px;padding:32px">
                <h1 style="color:#00C2FF;font-size:20px;margin:0 0 4px">VigiShield</h1>
                <p style="color:#9CA3AF;font-size:12px;margin:0 0 24px;letter-spacing:2px">SEGURIDAD INTELIGENTE</p>
                <p style="color:#E5E7EB;font-size:15px;line-height:1.6;margin:0 0 16px">
                  {de} te ha invitado a la vigilancia de su vivienda.
                </p>
                <p style="color:#E5E7EB;font-size:15px;line-height:1.6;margin:0 0 24px">
                  Al aceptar podrás ver el video en vivo, el historial de eventos y sus
                  evidencias. No podrás modificar cámaras, rostros ni la configuración:
                  eso queda en manos del residente principal. La invitación caduca en 7 días.
                </p>
                <p style="margin:0 0 24px">
                  <a href="{inviteUrl}" style="display:inline-block;background:#00C2FF;color:#0A0F1E;
                     text-decoration:none;font-weight:700;font-size:15px;padding:14px 28px;border-radius:12px">
                    Aceptar invitación
                  </a>
                </p>
                <p style="color:#9CA3AF;font-size:13px;line-height:1.6;margin:0 0 8px">
                  Si el botón no funciona, copia esta dirección en tu navegador:
                </p>
                <p style="color:#00C2FF;font-size:12px;word-break:break-all;margin:0 0 24px">{inviteUrl}</p>
                <p style="color:#6B7280;font-size:12px;line-height:1.6;margin:0;border-top:1px solid #1F2937;padding-top:16px">
                  Si no esperabas esta invitación, ignora este mensaje: sin aceptarla no se crea ninguna cuenta.
                </p>
              </div>
            </div>
            """;
        return SendAsync(to, "Te invitaron a VigiShield", html);
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***";
        return email[0] + new string('*', at - 1) + email[at..];
    }
}
