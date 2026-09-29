using StorageExplorer.Web.Tables;

namespace StorageExplorer.Web.Tests;

public class EntityPropertyParserTests
{
    [Fact]
    public void TryParse_NoProperties_ReturnsTrueWithAnEmptyBag()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([], out var parsed, out var error);

        // Assert
        Assert.True(ok);
        Assert.Empty(parsed);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-7", -7L)]
    [InlineData("3.14", 3.14)]
    [InlineData("1e3", 1000.0)]
    public void TryParse_Number_ParsesWholeNumbersAsLongAndTheRestAsDouble(string value, object expected)
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Price", "Number", value)], out var parsed, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, parsed["Price"]);
        Assert.IsType(expected.GetType(), parsed["Price"]);
    }

    [Fact]
    public void TryParse_NumberNotANumber_ReturnsFalseWithAnErrorNamingTheProperty()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Price", "Number", "abc")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.Contains("Price", error);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void TryParse_Boolean_Parses(string value, bool expected)
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("InStock", "Boolean", value)], out var parsed, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, parsed["InStock"]);
    }

    [Fact]
    public void TryParse_BooleanNotABoolean_ReturnsFalse()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("InStock", "Boolean", "yes")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_DateTime_ParsesToADateTimeOffset()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Due", "DateTime", "2024-01-01T12:00:00Z")], out var parsed, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), parsed["Due"]);
    }

    [Fact]
    public void TryParse_DateTimeNotADate_ReturnsFalse()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Due", "DateTime", "not a date")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_Guid_ParsesToAGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Id", "Guid", guid.ToString())], out var parsed, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(guid, parsed["Id"]);
    }

    [Fact]
    public void TryParse_GuidNotAGuid_ReturnsFalse()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Id", "Guid", "not-a-guid")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.Contains("not-a-guid", error);
    }

    [Fact]
    public void TryParse_String_KeepsTheValueAsIsAndTreatsMissingAsEmpty()
    {
        // Act
        var ok = EntityPropertyParser.TryParse(
            [new EntityPropertyInput("Name", "String", "Widget"), new EntityPropertyInput("Note", "String", null)],
            out var parsed,
            out _);

        // Assert
        Assert.True(ok);
        Assert.Equal("Widget", parsed["Name"]);
        Assert.Equal("", parsed["Note"]);
    }

    [Fact]
    public void TryParse_UnknownType_ReturnsFalse()
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput("Name", "Array", "x")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.Contains("Array", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void TryParse_BlankName_ReturnsFalse(string? name)
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput(name!, "String", "x")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.Equal("Every property needs a name.", error);
    }

    [Theory]
    [InlineData("PartitionKey")]
    [InlineData("RowKey")]
    [InlineData("Timestamp")]
    [InlineData("odata.etag")]
    public void TryParse_ReservedName_ReturnsFalse(string name)
    {
        // Act
        var ok = EntityPropertyParser.TryParse([new EntityPropertyInput(name, "String", "x")], out _, out var error);

        // Assert
        Assert.False(ok);
        Assert.Contains(name, error);
    }
}
