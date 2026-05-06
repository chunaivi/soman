using CommunityToolkit.Mvvm.ComponentModel;
using SoMan.Models;

namespace SoMan.ViewModels;

/// <summary>
/// Lightweight wrapper around <see cref="Account"/> that adds a transient
/// IsSelected flag so the Tasks page multi-select picker can bind without
/// mutating the underlying Account model.
/// </summary>
public partial class AccountPick : ObservableObject
{
    public Account Account { get; }

    [ObservableProperty]
    private bool _isSelected;

    public int Id => Account.Id;
    public string Name => Account.Name;
    public string Username => Account.Username;

    public AccountPick(Account account, bool isSelected = false)
    {
        Account = account;
        _isSelected = isSelected;
    }
}

/// <summary>
/// Wrapper for AccountCategory that exposes how many accounts the category
/// contains, used by the Categories tab of the Tasks picker.
/// </summary>
public partial class CategoryPick : ObservableObject
{
    public AccountCategory Category { get; }
    public int AccountCount { get; }

    public int Id => Category.Id;
    public string Name => Category.Name;
    public string Color => Category.Color;

    public CategoryPick(AccountCategory category, int accountCount)
    {
        Category = category;
        AccountCount = accountCount;
    }
}
