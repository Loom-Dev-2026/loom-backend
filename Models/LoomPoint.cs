namespace Loom.Models;

public class LoomPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public LoomPoint() { }
    public LoomPoint(double x, double y) { X = x; Y = y; }

    public override string ToString() => $"({X:F1}, {Y:F1})";
}