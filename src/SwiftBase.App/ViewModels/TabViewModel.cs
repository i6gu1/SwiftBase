using System.Windows.Input;
using SwiftBase.Models;

namespace SwiftBase.ViewModels;

public abstract class TabViewModel : ObservableObject
{
    private string _title;

    protected TabViewModel(Action<TabViewModel> close, string title, string glyph, string subtitle)
    {
        _title = title;
        Glyph = glyph;
        Subtitle = subtitle;
        CloseCommand = new RelayCommand(() => close(this));
    }

    public string Title
    {
        get => _title;
        protected set => Set(ref _title, value);
    }

    public string Glyph { get; }
    public virtual string Subtitle { get; }
    public virtual bool IsQueryTab => false;
    public virtual bool HasPendingChanges => false;
    public ICommand CloseCommand { get; }

    public virtual void OnClosed()
    {
    }
}
