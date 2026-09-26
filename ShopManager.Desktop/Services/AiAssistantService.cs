using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس دستیار هوشمند DeepSeek
/// با API رسمی DeepSeek کار می‌کنه (سازگار با OpenAI)
/// </summary>
public class AiAssistantService
{
    private const string ApiUrl = "https://api.deepseek.com/chat/completions";
    private static readonly HttpClient _http = new HttpClient();

    private readonly string _apiKey;

    public AiAssistantService(string apiKey)
    {
        _apiKey = apiKey;
    }

    /// <summary>
    /// ارسال پیام به DeepSeek و دریافت پاسخ
    /// </summary>
    public async Task<string> SendMessageAsync(string userMessage, string? codeContext = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return "⚠️ API Key تنظیم نشده. لطفاً اول کلید DeepSeek خودت رو وارد کن.";
            }

            // ─── ساخت پیام‌ها ───
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = "You are an expert Avalonia UI, C#, and .NET developer. " +
                              "Help the user modify and improve their ShopManager desktop application. " +
                              "Answer in Persian (فارسی) language. " +
                              "Provide complete code when asked, without partial snippets."
                }
            };

            if (!string.IsNullOrWhiteSpace(codeContext))
            {
                messages.Add(new
                {
                    role = "user",
                    content = $"Context (current code):\n\n{codeContext}\n\nTask: {userMessage}"
                });
            }
            else
            {
                messages.Add(new
                {
                    role = "user",
                    content = userMessage
                });
            }

            // ─── بدنه درخواست ───
            var body = new
            {
                model = "deepseek-chat",
                messages = messages,
                temperature = 0.7,
                stream = false
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            // ─── ارسال ───
            var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"❌ خطا در ارتباط با DeepSeek\n\n" +
                       $"وضعیت: {(int)response.StatusCode}\n\n" +
                       $"پاسخ سرور:\n{responseBody}";
            }

            // ─── استخراج پاسخ ───
            using var doc = JsonDocument.Parse(responseBody);
            var reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return reply ?? "پاسخی دریافت نشد.";
        }
        catch (HttpRequestException ex)
        {
            return $"❌ خطای شبکه: {ex.Message}\n\n" +
                   "اتصال اینترنت خود را بررسی کن.";
        }
        catch (Exception ex)
        {
            return $"❌ خطا: {ex.Message}";
        }
    }
}