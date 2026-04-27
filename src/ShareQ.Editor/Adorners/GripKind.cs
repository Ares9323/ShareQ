namespace ShareQ.Editor.Adorners;

public enum GripKind
{
    None,
    // 8-grip resize for rect/ellipse
    TopLeft, Top, TopRight,
    Left, Right,
    BottomLeft, Bottom, BottomRight,
    // 2-grip endpoint for line/arrow
    From, To,
    // Single grip for text font-size / stepcounter radius
    Resize,
    // Rotation grip floating above the shape (rect / ellipse / text)
    Rotate
}
