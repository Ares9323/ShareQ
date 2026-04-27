using System.Collections.ObjectModel;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Commands;

public interface IEditorCommand
{
    void Apply(ObservableCollection<Shape> shapes);
    void Undo(ObservableCollection<Shape> shapes);
}
