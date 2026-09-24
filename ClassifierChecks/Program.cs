using ZimbabweTenderAPI.Services;

var fixtures = new (string Title, string ExpectedSector)[]
{
    ("Supply of laptops and network equipment", "ICT & Software"),
    ("Supply and delivery of medical equipment", "Healthcare & Medical"),
    ("Construction of a district hospital", "Civil & Infrastructure"),
    ("Supply of solar panels and inverters", "Electrical & Energy"),
    ("Supply of office furniture and stationery", "General Goods & Consumables"),
    ("Consultancy services for an external audit", "Services & Logistics"),
    ("Invitation to tender for annual requirements", "Other"),
};

var failures = fixtures
    .Select(fixture => (fixture, Actual: TenderClassificationRules.Classify(fixture.Title)))
    .Where(result => result.Actual.Sector != result.fixture.ExpectedSector)
    .ToArray();

if (TenderClassificationRules.Classify("Annual supply contract").Sector != "Other")
{
    failures = failures.Append((("Annual supply contract", "Other"), TenderClassificationRules.Classify("Annual supply contract"))).ToArray();
}

if (failures.Length > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine($"{failure.fixture.Title}: expected {failure.fixture.ExpectedSector}, got {failure.Actual.Sector}");
    return 1;
}

Console.WriteLine($"Classifier checks passed ({fixtures.Length + 1} cases).");
return 0;
