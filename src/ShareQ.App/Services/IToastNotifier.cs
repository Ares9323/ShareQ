namespace ShareQ.App.Services;

public interface IToastNotifier
{
    void Show(string title, string message);
}
