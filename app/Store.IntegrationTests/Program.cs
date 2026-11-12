using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Store.Web.Data;
using Store.Web.Models;
using Store.Web.Services;

if (args.Contains("--offline")) return await ManagerSessionChecks.RunAsync();

var connectionString = Environment.GetEnvironmentVariable("StoreTestConnectionString");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set StoreTestConnectionString to the limited manager connection in a dedicated test database.");
    return 2;
}
var secondConnectionString = Environment.GetEnvironmentVariable("StoreTestManager2ConnectionString");
if (string.IsNullOrWhiteSpace(secondConnectionString))
{
    Console.Error.WriteLine("Run scripts/test-app.ps1 to configure both limited test managers.");
    return 2;
}
var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
if (databaseName is null || !System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^store_webtests(?:_[a-z0-9_]+)?$") ||
    databaseName != new NpgsqlConnectionStringBuilder(secondConnectionString).Database)
    throw new InvalidOperationException("Tests require two managers in the same dedicated store_webtests database.");

var reportDirectory = Path.Combine(FindProjectRoot(), "reports");
Directory.CreateDirectory(reportDirectory);
var results = new List<TestResult>();
await using var source = NpgsqlDataSource.Create(connectionString);
var suite = new IntegrationSuite(source, new NpgsqlConnectionStringBuilder(connectionString).Username ?? "", connectionString, secondConnectionString);

try
{
    await suite.SetupAsync();
}
catch (Exception ex)
{
    results.Add(new("Isolated fixture setup", false, 0, ex.Message));
    Console.Error.WriteLine("FAIL fixture setup: " + ex.Message);
    await WriteReportAsync();
    return 1;
}

foreach (var test in suite.Tests())
{
    var timer = Stopwatch.StartNew();
    try
    {
        await test.Body();
        results.Add(new(test.Name, true, timer.ElapsedMilliseconds, null));
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception ex)
    {
        results.Add(new(test.Name, false, timer.ElapsedMilliseconds, ex.Message));
        Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message);
    }
}

await WriteReportAsync();
Console.WriteLine($"{results.Count(x => x.Passed)}/{results.Count} application integration tests passed.");
return results.All(x => x.Passed) ? 0 : 1;

async Task WriteReportAsync()
{
    var report = new
    {
        ExecutedAt = DateTimeOffset.UtcNow,
        Database = new NpgsqlConnectionStringBuilder(connectionString).Database,
        LimitedUser = new NpgsqlConnectionStringBuilder(connectionString).Username,
        FixturePrefix = suite.Prefix,
        FixtureProductIds = suite.ProductIds,
        Passed = results.Count(x => x.Passed),
        Total = results.Count,
        Results = results
    };
    await File.WriteAllTextAsync(Path.Combine(reportDirectory, "app-integration-tests.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

static string FindProjectRoot()
{
    for (DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "task.txt")))
            return dir.FullName;
    for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "task.txt")))
            return dir.FullName;
    throw new InvalidOperationException("Cannot locate the project root containing task.txt.");
}

sealed record TestResult(string Name, bool Passed, long Milliseconds, string? Error);
sealed record IntegrationTest(string Name, Func<Task> Body);
sealed record DocumentCounts(long Sales, long Lines, long Payments);

sealed class IntegrationSuite(NpgsqlDataSource source, string expectedUsername, string primaryConnection, string secondConnection)
{
    public string Prefix { get; } = "IT-" + Guid.NewGuid().ToString("N")[..16];
    private long _piece;
    private long _weight;
    private long _scarce;
    private long _offer;
    private long _category;
    private long _department;
    private long _secondDepartment;
    private long _supplier;
    private int _documentSequence;
    public long[] ProductIds => new[] { _piece, _weight, _scarce, _offer }.Where(id => id > 0).ToArray();

    public async Task SetupAsync()
    {
        await WithService(async service =>
        {
            var user = Require(await service.CurrentUserAsync(), "The manager has no active store.users mapping.");
            Equal(expectedUsername, user.Username, "Unexpected PostgreSQL actor.");
            Equal("manager", user.RoleName, "Tests require an actual limited manager account.");
            var departments = await service.DepartmentsAsync();
            Check(departments.Count >= 2, "At least two demo departments are required.");
            _department = departments.OrderBy(x => x.Id).First().Id;
            _secondDepartment = departments.OrderBy(x => x.Id).Skip(1).First().Id;
            _category = (await service.CategoriesAsync()).First().Id;
            _supplier = (await service.SuppliersAsync()).First().Id;
            _piece = await service.SaveProductAsync(NewProduct("PIECE", "pcs", 12.34m));
            _weight = await service.SaveProductAsync(NewProduct("WEIGHT", "kg", 8.25m));
            _scarce = await service.SaveProductAsync(NewProduct("SCARCE", "pcs", 20m));
        });

        // Fixture preparation uses the same service and stored-procedure API as the application.
        await WithService(async service =>
        {
            foreach (var product in ProductIds)
            {
                await service.SetSupplierProductAsync(new SupplierProductInput
                {
                    ProductId = product, SupplierId = _supplier, PurchasePrice = 5m
                });
            }
        });

        await WithService(async service =>
        {
            await service.ReceiveAsync(new SupplyInput
            {
                SupplierId = _supplier,
                DocumentNumber = Number("SEED"),
                Lines =
                [
                    new() { DepartmentId = _department, ProductId = _piece, Quantity = 30 },
                    new() { DepartmentId = _department, ProductId = _weight, Quantity = 20 },
                    new() { DepartmentId = _secondDepartment, ProductId = _scarce, Quantity = 1 }
                ]
            });
        });
    }

    public IEnumerable<IntegrationTest> Tests()
    {
        yield return new("Checkout commits header, multiple lines, split payments and stock", CheckoutSuccessAsync);
        yield return new("Late stock failure rolls back header, lines, payments and first stock change", InsufficientStockRollbackAsync);
        yield return new("Duplicate payment reference rolls back all new document records", DuplicatePaymentRollbackAsync);
        yield return new("Repeated receipt cannot double-charge or decrement stock twice", DuplicateReceiptAsync);
        yield return new("Soft delete hides product, retains history and supports restore", SoftDeleteRestoreAsync);
        yield return new("Product updates use the actual PostgreSQL manager identity for audit", AuditIdentityAsync);
        yield return new("Supply posts multiple lines and increases the correct department stocks", SupplySuccessAsync);
        yield return new("Supplier price create/update controls default supply cost and preserves history", SupplierPriceAsync);
        yield return new("Explicit Unit of Work rollback spans sales, payments and inventory repositories", ExplicitRollbackAsync);
        yield return new("Rejected nested service transaction preserves caller changes until explicit rollback", NestedServiceTransactionAsync);
        yield return new("Disposing an uncommitted Unit of Work reverses a posted sale and stock", DisposeRollbackAsync);
        yield return new("Failed operation leaves the scoped Unit of Work usable for the next operation", RecoveryAfterFailureAsync);
        yield return new("Manager rename persists, audits the current user and leaves the other manager unchanged", ManagerRenameAsync);
        yield return new("Browser switch uses the selected database login for product audit and checkout", ManagerSwitchAsync);
    }

    private async Task CheckoutSuccessAsync()
    {
        var beforePiece = await StockAsync(_department, _piece);
        var beforeWeight = await StockAsync(_department, _weight);
        var input = new CheckoutInput
        {
            ReceiptNumber = Number("SUCCESS"),
            Lines =
            [
                new() { DepartmentId = _department, ProductId = _piece, Quantity = 2 },
                new() { DepartmentId = _department, ProductId = _weight, Quantity = 1.25m, DiscountAmount = 0.30m }
            ],
            Payments =
            [
                new() { Method = "cash", Amount = 10 },
                new() { Method = "card", Amount = 24.69m, Reference = Number("PAY") }
            ]
        };
        var id = await WithService(service => service.CheckoutAsync(input));
        var details = Require(await WithService(service => service.SaleAsync(id)), "Posted sale cannot be read.");
        Equal("posted", details.Sale.Status, "Sale must be posted.");
        Equal(2, details.Lines.Count, "Wrong number of committed lines.");
        Equal(2, details.Payments.Count, "Wrong number of committed payments.");
        Equal(34.69m, details.Sale.Total, "Decimal rounding or discount is wrong.");
        Equal(details.Sale.Total, details.Sale.Paid, "Split payment does not match total.");
        Equal(0m, details.Sale.Balance, "Paid sale has a balance.");
        Equal(expectedUsername, details.Sale.Cashier, "Cashier must be the actual connection identity.");
        Equal(beforePiece.Quantity - 2, (await StockAsync(_department, _piece)).Quantity, "Piece stock was not decremented.");
        Equal(beforeWeight.Quantity - 1.25m, (await StockAsync(_department, _weight)).Quantity, "Weight stock was not decremented.");
    }

    private async Task InsufficientStockRollbackAsync()
    {
        var before = await CountsAsync();
        var firstStock = await StockAsync(_department, _piece);
        var scarceStock = await StockAsync(_secondDepartment, _scarce);
        var tooMany = scarceStock.Quantity + 1;
        var input = new CheckoutInput
        {
            ReceiptNumber = Number("NOSTOCK"),
            Lines =
            [
                new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 },
                new() { DepartmentId = _secondDepartment, ProductId = _scarce, Quantity = tooMany }
            ],
            Payments = [new() { Method = "cash", Amount = 12.34m + tooMany * 20m }]
        };
        await ExpectDatabaseFailure(() => WithService(service => service.CheckoutAsync(input)), "23514");
        Equal(before, await CountsAsync(), "Failed checkout left document rows behind.");
        Check(!await ReceiptExistsAsync(input.ReceiptNumber), "Failed receipt still exists.");
        Equal(firstStock.Quantity, (await StockAsync(_department, _piece)).Quantity, "Earlier stock update did not roll back.");
        Equal(scarceStock.Quantity, (await StockAsync(_secondDepartment, _scarce)).Quantity, "Unavailable stock changed.");
    }

    private async Task DuplicatePaymentRollbackAsync()
    {
        var reference = Number("DUPPAY");
        await WithService(service => service.CheckoutAsync(OnePiece(Number("REF-SEED"), reference)));
        var before = await CountsAsync();
        var stock = await StockAsync(_department, _piece);
        var input = new CheckoutInput
        {
            ReceiptNumber = Number("DUPPAY-FAIL"),
            Lines = [new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 }],
            Payments =
            [
                new() { Method = "cash", Amount = 5 },
                new() { Method = "card", Amount = 7.34m, Reference = reference }
            ]
        };
        await ExpectDatabaseFailure(() => WithService(service => service.CheckoutAsync(input)), "23505");
        Equal(before, await CountsAsync(), "Header, line or the first payment survived a later payment failure.");
        Check(!await ReceiptExistsAsync(input.ReceiptNumber), "Failed receipt still exists.");
        Equal(stock.Quantity, (await StockAsync(_department, _piece)).Quantity, "Payment failure changed stock.");
    }

    private async Task DuplicateReceiptAsync()
    {
        var input = OnePiece(Number("ONCE"));
        var id = await WithService(service => service.CheckoutAsync(input));
        var before = await CountsAsync();
        var stock = await StockAsync(_department, _piece);
        await ExpectDatabaseFailure(() => WithService(service => service.CheckoutAsync(input)), "23505");
        Equal(before, await CountsAsync(), "Repeated receipt created extra records.");
        Equal(stock.Quantity, (await StockAsync(_department, _piece)).Quantity, "Repeated receipt decremented stock again.");
        Equal("posted", Require(await WithService(service => service.SaleAsync(id)), "Original receipt missing.").Sale.Status,
            "Original receipt changed.");
    }

    private async Task SoftDeleteRestoreAsync()
    {
        var id = await WithService(service => service.CheckoutAsync(OnePiece(Number("HISTORY"))));
        var stockBefore = await StockAsync(_department, _piece);
        await WithService(service => service.DeleteProductAsync(_piece));
        try
        {
            Check(await WithService(service => service.ProductAsync(_piece)) is null, "Deleted product appears active.");
            Check((await WithService(service => service.ArchiveAsync())).Any(x => x.Id == _piece), "Deleted product missing from archive.");
            var history = Require(await WithService(service => service.SaleAsync(id)), "Historical receipt disappeared.");
            Equal(_piece, history.Lines.Single().ProductId, "Historical sale line disappeared.");
            Equal(12.34m, history.Lines.Single().UnitPrice, "Historical price changed.");
        }
        finally
        {
            await WithService(service => service.RestoreProductAsync(_piece));
        }
        Check(await WithService(service => service.ProductAsync(_piece)) is not null, "Restored product not visible.");
        Equal(stockBefore.Quantity, (await StockAsync(_department, _piece)).Quantity, "Soft deletion changed stock.");
    }

    private async Task AuditIdentityAsync()
    {
        var original = Require(await WithService(service => service.ProductAsync(_piece)), "Fixture product missing.");
        var current = Require(await WithService(service => service.CurrentUserAsync()), "Current user not resolved.");
        await Task.Delay(10);
        await WithService(service => service.SaveProductAsync(new ProductInput
        {
            Id = original.Id, CategoryId = original.CategoryId, Sku = original.Sku,
            Barcode = original.Barcode, Name = original.Name + " updated", Unit = original.Unit,
            SalePrice = original.SalePrice
        }));
        var updated = Require(await WithService(service => service.ProductAsync(_piece)), "Updated product missing.");
        Equal(expectedUsername, current.Username, "Wrong current user.");
        Equal(current.Id, updated.UpdatedBy, "Audit author is not the actual manager.");
        Check(updated.UpdatedAt > original.UpdatedAt, "Audit date did not advance.");
    }

    private async Task SupplySuccessAsync()
    {
        var piece = await StockAsync(_department, _piece);
        var weight = await StockAsync(_department, _weight);
        var id = await WithService(service => service.ReceiveAsync(new SupplyInput
        {
            SupplierId = _supplier, DocumentNumber = Number("SUPPLY"),
            Lines =
            [
                new() { DepartmentId = _department, ProductId = _piece, Quantity = 3, UnitCost = 6.20m },
                new() { DepartmentId = _department, ProductId = _weight, Quantity = 2.5m, UnitCost = null }
            ]
        }));
        var details = Require(await WithService(service => service.SupplyAsync(id)), "Supply cannot be read.");
        Equal("posted", details.Supply.Status, "Supply is not posted.");
        Equal(2, details.Lines.Count, "Supply line count wrong.");
        Equal(31.10m, details.Supply.Total, "Supply total does not use explicit/default purchase prices.");
        Equal(piece.Quantity + 3, (await StockAsync(_department, _piece)).Quantity, "Piece replenishment wrong.");
        Equal(weight.Quantity + 2.5m, (await StockAsync(_department, _weight)).Quantity, "Weight replenishment wrong.");
    }

    private async Task SupplierPriceAsync()
    {
        _offer = await WithService(service => service.SaveProductAsync(NewProduct("OFFER", "pcs", 17m)));
        await WithService(service => service.SetSupplierProductAsync(new SupplierProductInput
        {
            ProductId = _offer, SupplierId = _supplier, PurchasePrice = 4.10m
        }));
        var createdOffer = (await WithService(service => service.SupplierProductsAsync()))
            .Single(x => x.ProductId == _offer && x.SupplierId == _supplier);
        Equal(4.10m, createdOffer.PurchasePrice, "Created supplier price is not visible through the lookup view.");

        var firstId = await ReceiveDefaultAsync(2);
        var first = Require(await WithService(service => service.SupplyAsync(firstId)), "First default-price supply missing.");
        Equal("posted", first.Supply.Status, "First default-price supply is not posted.");
        Equal(4.10m, first.Lines.Single().UnitCost, "Supply did not resolve the created supplier price.");
        Equal(8.20m, first.Supply.Total, "First default-price supply total is incorrect.");

        await WithService(service => service.SetSupplierProductAsync(new SupplierProductInput
        {
            ProductId = _offer, SupplierId = _supplier, PurchasePrice = 6.35m
        }));
        var updatedOffer = (await WithService(service => service.SupplierProductsAsync()))
            .Single(x => x.ProductId == _offer && x.SupplierId == _supplier);
        Equal(6.35m, updatedOffer.PurchasePrice, "Updated supplier price was not persisted as a single offer.");

        var secondId = await ReceiveDefaultAsync(3);
        var second = Require(await WithService(service => service.SupplyAsync(secondId)), "Second default-price supply missing.");
        Equal("posted", second.Supply.Status, "Second default-price supply is not posted.");
        Equal(6.35m, second.Lines.Single().UnitCost, "Later supply did not resolve the updated supplier price.");
        Equal(19.05m, second.Supply.Total, "Updated default-price supply total is incorrect.");
        Equal(5m, (await StockAsync(_department, _offer)).Quantity, "Both supplies did not replenish the isolated product.");

        var historical = Require(await WithService(service => service.SupplyAsync(firstId)), "Historical supply missing.");
        Equal(4.10m, historical.Lines.Single().UnitCost, "Supplier price update rewrote historical line cost.");
        Equal(8.20m, historical.Supply.Total, "Supplier price update rewrote historical supply total.");

        Task<long> ReceiveDefaultAsync(decimal quantity) => WithService(service => service.ReceiveAsync(new SupplyInput
        {
            SupplierId = _supplier, DocumentNumber = Number("OFFER"),
            Lines = [new() { DepartmentId = _department, ProductId = _offer, Quantity = quantity, UnitCost = null }]
        }));
    }

    private async Task NestedServiceTransactionAsync()
    {
        var before = await CountsAsync();
        var stock = await StockAsync(_department, _piece);
        var callerReceipt = Number("CALLER");
        var nestedReceipt = Number("NESTED");
        await using (var uow = NewUnitOfWork())
        {
            await uow.BeginAsync();
            var id = await uow.Sales.CreateAsync(callerReceipt, null);
            await uow.Sales.AddLineAsync(id, new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 });
            await uow.Payments.AddAsync(id, new() { Method = "cash", Amount = 12.34m });
            await uow.Inventory.SetMinimumAsync(_department, _piece, stock.MinimumQuantity + 1);

            InvalidOperationException? rejection = null;
            try { await new StoreService(uow).CheckoutAsync(OnePiece(nestedReceipt)); }
            catch (InvalidOperationException ex) { rejection = ex; }
            Check(rejection is not null, "Nested service transaction unexpectedly succeeded.");
            Equal("A transaction is already active.", rejection!.Message, "Nested call failed for an unrelated reason.");

            Equal(callerReceipt, Require(await uow.Sales.GetAsync(id), "Nested call rolled back the caller header.").ReceiptNumber,
                "Nested call changed the caller header.");
            Equal(1, (await uow.Sales.LinesAsync(id)).Count, "Nested call rolled back the caller line.");
            Equal(1, (await uow.Payments.ListAsync(id)).Count, "Nested call rolled back the caller payment.");
            Equal(stock.MinimumQuantity + 1, (await uow.Inventory.ListAsync(_department))
                .Single(x => x.ProductId == _piece).MinimumQuantity, "Nested call rolled back the caller stock setting.");
            Check(!await ReceiptExistsAsync(callerReceipt), "Nested call committed the caller transaction.");

            // Repository writes require an active transaction, proving it remains usable after rejection.
            await uow.Inventory.SetMinimumAsync(_department, _piece, stock.MinimumQuantity + 2);
            Equal(stock.MinimumQuantity + 2, (await uow.Inventory.ListAsync(_department))
                .Single(x => x.ProductId == _piece).MinimumQuantity, "Caller cannot continue its transaction.");
            await uow.RollbackAsync();
        }
        Equal(before, await CountsAsync(), "Caller rollback after nested rejection left document records behind.");
        Check(!await ReceiptExistsAsync(callerReceipt), "Caller receipt survived its explicit rollback.");
        Check(!await ReceiptExistsAsync(nestedReceipt), "Rejected nested receipt was created.");
        Equal(stock.MinimumQuantity, (await StockAsync(_department, _piece)).MinimumQuantity,
            "Caller rollback did not restore the stock setting.");
        Equal(stock.Quantity, (await StockAsync(_department, _piece)).Quantity, "Nested service rejection changed stock quantity.");
    }

    private async Task ExplicitRollbackAsync()
    {
        var before = await CountsAsync();
        var stock = await StockAsync(_department, _piece);
        var receipt = Number("ROLLBACK");
        await using (var uow = NewUnitOfWork())
        {
            await uow.BeginAsync();
            var id = await uow.Sales.CreateAsync(receipt, null);
            await uow.Sales.AddLineAsync(id, new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 });
            await uow.Payments.AddAsync(id, new() { Method = "cash", Amount = 12.34m });
            await uow.Inventory.SetMinimumAsync(_department, _piece, stock.MinimumQuantity + 1);
            Check(await uow.Sales.GetAsync(id) is not null, "Sale repository cannot see its uncommitted header.");
            Equal(1, (await uow.Payments.ListAsync(id)).Count, "Payment repository is not sharing the transaction.");
            Equal(stock.MinimumQuantity + 1, (await uow.Inventory.ListAsync(_department))
                .Single(x => x.ProductId == _piece).MinimumQuantity, "Inventory repository is not sharing the transaction.");
            await uow.RollbackAsync();
        }
        Equal(before, await CountsAsync(), "Explicit rollback left records behind.");
        Check(!await ReceiptExistsAsync(receipt), "Explicitly rolled-back receipt exists.");
        Equal(stock.MinimumQuantity, (await StockAsync(_department, _piece)).MinimumQuantity, "Minimum-stock update was not rolled back.");
    }

    private async Task DisposeRollbackAsync()
    {
        var before = await CountsAsync();
        var stock = await StockAsync(_department, _piece);
        var receipt = Number("DISPOSE");
        await using (var uow = NewUnitOfWork())
        {
            await uow.BeginAsync();
            var id = await uow.Sales.CreateAsync(receipt, null);
            await uow.Sales.AddLineAsync(id, new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 });
            await uow.Payments.AddAsync(id, new() { Method = "cash", Amount = 12.34m });
            await uow.Sales.PostAsync(id);
            Equal("posted", Require(await uow.Sales.GetAsync(id), "Uncommitted posted sale missing.").Status,
                "The scenario did not reach posting before disposal.");
            // No Commit or Rollback: DisposeAsync must roll back the transaction.
        }
        Equal(before, await CountsAsync(), "Disposal left posted document records behind.");
        Check(!await ReceiptExistsAsync(receipt), "Disposed receipt exists.");
        Equal(stock.Quantity, (await StockAsync(_department, _piece)).Quantity, "Disposal did not roll back stock.");
    }

    private async Task RecoveryAfterFailureAsync()
    {
        await using var uow = NewUnitOfWork();
        var service = new StoreService(uow);
        var tooMany = (await service.InventoryAsync(_department)).Single(x => x.ProductId == _piece).Quantity + 1;
        var failed = OnePiece(Number("RECOVER-FAIL"));
        failed.Lines[0].Quantity = tooMany;
        failed.Payments[0].Amount = tooMany * 12.34m;
        await ExpectDatabaseFailure(() => service.CheckoutAsync(failed), "23514");
        var id = await service.CheckoutAsync(OnePiece(Number("RECOVER-OK")));
        Equal("posted", Require(await service.SaleAsync(id), "Subsequent checkout missing.").Sale.Status,
            "Failed transaction poisoned the scoped Unit of Work.");
    }

    private async Task ManagerRenameAsync()
    {
        var before = (await WithService(s => s.ManagersAsync())).ToDictionary(m => m.Username);
        var current = before[expectedUsername];
        var newName = "Тестовий менеджер " + Prefix;
        try
        {
            await WithService(s => s.RenameCurrentManagerAsync(new() { FullName = "  " + newName + "  " }));
            var after = (await WithService(s => s.ManagersAsync())).ToDictionary(m => m.Username);
            Equal(newName, after[expectedUsername].FullName, "The trimmed name was not persisted.");
            Equal(current.Id, after[expectedUsername].UpdatedBy, "Rename audit used the wrong actor.");
            Check(after[expectedUsername].UpdatedAt > current.UpdatedAt, "Rename audit time was not updated.");
            Equal(newName, Require(await WithService(s => s.CurrentUserAsync()), "Current user missing.").FullName,
                "The current-user view still shows the old name.");
            foreach (var other in before.Values.Where(m => m.Username != expectedUsername))
                Equal(other, after[other.Username], "Renaming one manager changed another profile.");
            await using var work = NewUnitOfWork();
            await work.BeginAsync();
            await ExpectDatabaseFailure(() => work.Lookups.RenameCurrentManagerAsync("   "), "23514");
            await work.RollbackAsync();
            await work.BeginAsync();
            await ExpectDatabaseFailure(() => work.Lookups.RenameCurrentManagerAsync(new string('a', 201)), "23514");
            await work.RollbackAsync();
        }
        finally
        {
            await WithService(s => s.RenameCurrentManagerAsync(new() { FullName = current.FullName }));
        }
    }

    private async Task ManagerSwitchAsync()
    {
        await using var registry = new ManagerRegistry(primaryConnection, [secondConnection]);
        var protection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        var browser = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var session = new ManagerSession(new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = browser }, registry, protection);
        var secondUsername = new NpgsqlConnectionStringBuilder(secondConnection).Username!;
        await session.SwitchAsync(secondUsername);
        Equal(expectedUsername, session.Username, "An in-flight request changed database actor.");
        var cookie = browser.Response.Headers.SetCookie.Single(h => h!.StartsWith(ManagerSession.CookieName + "="))!;
        Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase),
            "Manager cookie protection flags missing.");
        var nextRequest = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        nextRequest.Request.Headers.Cookie = cookie.Split(';')[0];
        var selected = new ManagerSession(new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = nextRequest }, registry, protection);
        Equal(secondUsername, selected.Username, "The redirected request ignored the chosen manager.");
        await using var work = new PostgreSqlUnitOfWork(registry.Source(selected.Username));
        var service = new StoreService(work);
        var user = Require(await service.CurrentUserAsync(), "Second manager missing.");
        Equal(secondUsername, user.Username, "Wrong actual PostgreSQL login after switching.");
        var product = NewProduct("SECOND-MANAGER", "pcs", 10m);
        var productId = await service.SaveProductAsync(product);
        Equal(user.Id, Require(await service.ProductAsync(productId), "New product missing.").UpdatedBy, "Product audit used the previous manager.");
        var saleId = await service.CheckoutAsync(OnePiece(Number("SECOND-MANAGER")));
        Equal(secondUsername, Require(await WithService(s => s.SaleAsync(saleId)), "Sale missing.").Sale.Cashier,
            "The sale author changed when read by another manager.");
        var otherBrowser = new ManagerSession(new Microsoft.AspNetCore.Http.HttpContextAccessor
            { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }, registry, protection);
        Equal(expectedUsername, otherBrowser.Username, "Switching affected a separate browser.");
    }

    private ProductInput NewProduct(string suffix, string unit, decimal price) => new()
    {
        CategoryId = _category, Sku = Prefix + "-" + suffix, Name = "Integration " + Prefix + " " + suffix,
        Unit = unit, SalePrice = price
    };

    private CheckoutInput OnePiece(string receipt, string? reference = null) => new()
    {
        ReceiptNumber = receipt,
        Lines = [new() { DepartmentId = _department, ProductId = _piece, Quantity = 1 }],
        Payments = [new() { Method = reference is null ? "cash" : "card", Amount = 12.34m, Reference = reference }]
    };

    private string Number(string purpose) => Prefix + "-" + purpose + "-" + Interlocked.Increment(ref _documentSequence);
    private PostgreSqlUnitOfWork NewUnitOfWork() => new(source);

    private async Task<T> WithService<T>(Func<IStoreService, Task<T>> action)
    {
        await using var uow = NewUnitOfWork();
        return await action(new StoreService(uow));
    }

    private async Task WithService(Func<IStoreService, Task> action)
    {
        await using var uow = NewUnitOfWork();
        await action(new StoreService(uow));
    }

    private async Task<Stock> StockAsync(long department, long product) =>
        (await WithService(service => service.InventoryAsync(department))).Single(x => x.ProductId == product);

    private async Task<DocumentCounts> CountsAsync()
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM store.v_sales_summary),
                   (SELECT count(*) FROM store.v_sale_items),
                   (SELECT count(*) FROM store.v_payments)
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Check(await reader.ReadAsync(), "Count query returned no row.");
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private async Task<bool> ReceiptExistsAsync(string receipt)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM store.v_sales_summary WHERE receipt_number = $1)", connection);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = receipt });
        return (bool)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing existence result."));
    }

    private static async Task ExpectDatabaseFailure(Func<Task> action, string expectedState)
    {
        try { await action(); }
        catch (Exception ex)
        {
            for (Exception? cursor = ex; cursor is not null; cursor = cursor.InnerException)
            {
                if (cursor is PostgresException postgres)
                {
                    Equal(expectedState, postgres.SqlState, "Unexpected PostgreSQL failure.");
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Expected a PostgreSQL {expectedState} failure, received {ex.GetType().Name}: {ex.Message}", ex);
        }
        throw new InvalidOperationException($"Expected PostgreSQL failure {expectedState}, but the operation succeeded.");
    }

    private static T Require<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }
}

