namespace EU5MapExplorer.Api.Data.Entities;

public class BorderRef
{
    public string Key { get; set; } = null!;
    public bool Reversed { get; set; }
}

public class BorderRing
{
    public BorderRef[] Borders { get; set; } = [];
}
