# Custom inspectors

Custom inspectors are trusted code. Registration order is fixed when creating ObjectInspector; higher Priority wins over other registrations and built-in behavior. CanHandle is cached per runtime type. Inspect returns a DisplayNode, not text.

```csharp
using System.Globalization;
using Zamay.Core;

var inspector = new ObjectInspector([new MoneyInspector()]);
var document = inspector.Inspect(new Money(12.5m, "EUR"));
new TextRenderer().Render(document, Console.Out);

public sealed record Money(decimal Amount, string Currency);
public sealed class MoneyInspector : IValueInspector {
    public int Priority => 100;
    public bool CanHandle(Type type) => type == typeof(Money);
    public DisplayNode Inspect(object value, InspectionContext context) {
        var money = (Money)value;
        var text = money.Amount.ToString(CultureInfo.InvariantCulture) + " " + money.Currency;
        text = context.Options.SensitiveData?.Sanitize(text) ?? text;
        return new ScalarNode(DisplayText.Truncate(text, context.Options.MaxStringLength),
            "Money", Truncated: text.Length > context.Options.MaxStringLength);
    }
}
```

For nested objects, check CanInspectChild before evaluating a getter and then call InspectChild. That method participates in the current path and budget. A custom inspector that reads arbitrary members itself is responsible for sensitive-name checks and bounded work. Do not keep traversal state in a shared inspector. To change registrations, create a new ObjectInspector.
