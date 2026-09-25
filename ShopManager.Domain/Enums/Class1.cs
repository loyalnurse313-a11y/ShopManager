namespace ShopManager.Domain.Enums;

/// <summary>
/// وضعیت پرداخت تراکنش
/// </summary>
public enum PaymentStatus
{
    /// <summary>نقدی (پول فیزیکی) — وارد صندوق می‌شود</summary>
    Cash = 0,

    /// <summary>نسیه — در مانده حساب‌ها ثبت می‌شود</summary>
    Credit = 1,

    /// <summary>کارتی (POS بانکی) — وارد حساب بانکی می‌شود، نه صندوق</summary>
    Card = 2
}