using System.Data;
using Zamay.Core;
using Zamay;

new { Id = 42, Name = "Алексей", Password = "secret", Url = new Uri("https://example.com") }.ToConsole();
Console.WriteLine();
new[] { 1, 2, 3 }.ToConsole(); Console.WriteLine();
var cycle = new Link(); cycle.Next = cycle; cycle.ToConsole(); Console.WriteLine();
new Throwing().ToConsole(); Console.WriteLine();
var inspector = new ObjectInspector([new MoneyInspector()]);
new TextRenderer().Render(inspector.Inspect(new Money(12.50m, "EUR")), Console.Out); Console.WriteLine();
var table = new DataTable("Users"); table.Columns.Add("Id", typeof(int)); table.Columns.Add("Name"); table.Rows.Add(42, "Алексей"); table.ToConsole(); Console.WriteLine();
await Numbers().ToConsoleAsync(asyncOptions: new() { EnumerateAsyncEnumerable = true, MaxItems = 5 }); Console.WriteLine();
static async IAsyncEnumerable<int> Numbers() { for (int i = 0; i < 10; i++) { await Task.Yield(); yield return i; } }
sealed class Link { public Link? Next { get; set; } }
sealed class Throwing { public int Value => throw new InvalidOperationException("Пример ошибки getter"); }
sealed record Money(decimal Amount, string Currency);
sealed class MoneyInspector : IValueInspector
{
    public int Priority => 100;
    public bool CanHandle(Type type) => type == typeof(Money);
    public DisplayNode Inspect(object value, InspectionContext context) { var m = (Money)value; return new ScalarNode(m.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + m.Currency, "Money"); }
}
