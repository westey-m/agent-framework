# Redaction

Log data redaction utilities built on `Microsoft.Extensions.Compliance.Redaction.Redactor`.

Provides `ReplacingRedactor`, an internal `Redactor` implementation that replaces
any input with a fixed replacement string (e.g. `"<redacted>"`).

To use this in your project, add the following to your `.csproj` file:

```xml
<PropertyGroup>
  <InjectSharedRedaction>true</InjectSharedRedaction>
</PropertyGroup>
```

You will also need to add a package reference to `Microsoft.Extensions.Compliance.Abstractions`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Compliance.Abstractions" />
</ItemGroup>
```
