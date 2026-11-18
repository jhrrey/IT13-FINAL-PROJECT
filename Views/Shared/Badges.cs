using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;
using VetCare.Models.Navigation;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>The small pills used in the users table, details panel and permission table.</summary>
public static class Badges
{
    /// <summary>Role pill: Administrator navy/pale blue, Veterinarian teal/mint, Cashier orange/light orange, Super Admin purple.</summary>
    public static View Role(int roleId, string roleName)
    {
        var (text, background) = roleId switch
        {
            RoleIds.SuperAdmin => ("Purple", "PurpleSoft"),
            RoleIds.Administrator => ("Navy", "PaleBlue"),
            RoleIds.Veterinarian => ("Teal", "Mint"),
            RoleIds.CashierStaff => ("Orange", "OrangeSoft"),
            _ => ("Navy", "NeutralSoft")
        };
        return Pill(roleName, text, background);
    }

    /// <summary>"Active" with a green dot, or "Inactive" with a gray dot.</summary>
    public static View Status(string status)
    {
        bool active = status == AccountStatus.Active;
        return new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Ellipse
                {
                    WidthRequest = 8,
                    HeightRequest = 8,
                    Fill = ThemeResources.Color(active ? "SuccessDot" : "NeutralDot"),
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = active ? "Active" : "Inactive",
                    FontFamily = "DMSansMedium",
                    FontSize = 14,
                    TextColor = ThemeResources.Color(active ? "Success" : "BodyText"),
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
    }

    /// <summary>"Synced" (check, green), "Pending" (clock, amber), "Conflict" (alert, red).</summary>
    public static View Sync(string syncStatus)
    {
        var (icon, color, label) = syncStatus switch
        {
            SyncStatuses.Pending => ("clock", "Warning", "Pending"),
            SyncStatuses.Conflict => ("circle-alert", "Danger", "Conflict"),
            _ => ("check", "Success", "Synced")
        };

        return new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new LucideIcon
                {
                    Icon = icon,
                    StrokeColor = ThemeResources.Color(color),
                    WidthRequest = 16,
                    HeightRequest = 16,
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = label,
                    FontFamily = "DMSansMedium",
                    FontSize = 13,
                    TextColor = ThemeResources.Color(color),
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
    }

    /// <summary>Permission cell: Full (mint), View (pale blue), Own (cyan), or a gray dash.</summary>
    public static View Permission(PermissionCell cell)
    {
        if (cell.Kind == PermissionKind.None)
            return new Label
            {
                Text = "—",
                FontSize = 15,
                TextColor = ThemeResources.Color("NeutralDot"),
                VerticalOptions = LayoutOptions.Center
            };

        string background = cell.Kind switch
        {
            PermissionKind.Full => "Mint",
            PermissionKind.View => "PaleBlue",
            _ => "Cyan"
        };
        return Pill(cell.Label, "Navy", background);
    }

    /// <summary>
    /// Appointment status pill: Scheduled pale blue, Checked In cyan, In Consultation amber,
    /// Completed mint, Cancelled light gray, No-show light orange.
    /// </summary>
    public static View AppointmentStatus(string status)
    {
        var (background, text) = Models.Entities.AppointmentStatuses.ColorsFor(status);
        return Pill(status ?? Models.Entities.AppointmentStatuses.Scheduled, text, background);
    }

    /// <summary>Bill status pill: Unpaid light orange, Partial amber, Paid mint with a check, Void light gray.</summary>
    public static View BillStatus(string status) => IconPill(status ?? Models.Entities.BillStatuses.Unpaid,
        status == Models.Entities.BillStatuses.Paid ? "check" : null, Models.Entities.BillStatuses.ColorsFor(status));

    /// <summary>Payment method pill with its icon (banknote, smartphone, credit-card, landmark).</summary>
    public static View PaymentMethod(string method) =>
        IconPill(method, Models.Entities.PaymentMethods.IconFor(method), ("CyanSoft", "Navy"));

    public static View IconPill(string text, string icon, (string Background, string Text) colors)
    {
        var content = new HorizontalStackLayout { Spacing = 5 };
        if (icon != null)
            content.Children.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(colors.Text), WidthRequest = 13, HeightRequest = 13, VerticalOptions = LayoutOptions.Center });
        content.Children.Add(new Label { Text = text, FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color(colors.Text), VerticalOptions = LayoutOptions.Center });
        return new Border
        {
            Padding = new Thickness(icon == null ? 10 : 9, 3, 10, 3),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = ThemeResources.Color(colors.Background),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = content
        };
    }

    /// <summary>
    /// Vaccine status pill with its icon: Up to date mint (check), Due soon amber (clock),
    /// Overdue light orange (alert), Completed pale blue (badge-check).
    /// </summary>
    public static View VaccineStatus(Helpers.VaccineStatus status)
    {
        var (text, background, color, icon) = Helpers.VaccineScheduleHelper.Style(status);
        return new Border
        {
            Padding = new Thickness(9, 3, 10, 3),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = ThemeResources.Color(background),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(color), WidthRequest = 13, HeightRequest = 13, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color(color), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
    }

    public static View Pill(string text, string textColorKey, string backgroundKey) => new Border
    {
        Padding = new Thickness(10, 3),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        BackgroundColor = ThemeResources.Color(backgroundKey),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Center,
        Content = new Label
        {
            Text = text,
            FontFamily = "DMSansBold",
            FontSize = 12,
            TextColor = ThemeResources.Color(textColorKey)
        }
    };

    /// <summary>Clinic status: Active mint with a dot, Pending amber with a clock, Inactive gray with pause, Rejected red with an x.</summary>
    public static View ClinicStatus(string status)
    {
        if (status == AccountStatus.Active)
        {
            return new Border
            {
                Padding = new Thickness(9, 3, 10, 3),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                BackgroundColor = ThemeResources.Color("Mint"),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                Content = new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Ellipse { WidthRequest = 7, HeightRequest = 7, Fill = ThemeResources.Color("Success"), VerticalOptions = LayoutOptions.Center },
                        new Label { Text = "Active", FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color("Success"), VerticalOptions = LayoutOptions.Center }
                    }
                }
            };
        }
        return status switch
        {
            AccountStatus.Pending => IconPill("Pending", "clock", ("WarningSoft", "Warning")),
            AccountStatus.Rejected => IconPill("Rejected", "x", ("DangerSoft", "DangerHover")),
            _ => IconPill(status ?? AccountStatus.Inactive, "circle-pause", ("NeutralSoft", "BodyText"))
        };
    }

    /// <summary>Sync direction: Push pale blue, Pull cyan.</summary>
    public static View SyncDirection(string direction) =>
        IconPill(direction, direction == "Push" ? "upload" : "download", (direction == "Push" ? "PaleBlue" : "Cyan", "Navy"));

    /// <summary>Sync result: Success mint, Failed light red.</summary>
    public static View SyncResult(string status) =>
        status == "Success" ? IconPill("Success", "check", ("Mint", "Success")) : IconPill(status, "x", ("DangerSoft", "DangerHover"));

    /// <summary>
    /// Calculated admin account status: Active mint with a dot, Inactive gray, Waiting for approval amber with a clock,
    /// Clinic inactive light red with a pause icon.
    /// </summary>
    public static View AdminStatus(string displayStatus) => displayStatus switch
    {
        Models.Entities.AccountDisplayStatus.Active => ClinicStatus(Models.AccountStatus.Active),
        Models.Entities.AccountDisplayStatus.WaitingForApproval => IconPill("Waiting for approval", "clock", ("WarningSoft", "Warning")),
        Models.Entities.AccountDisplayStatus.ClinicInactive => IconPill("Clinic inactive", "circle-pause", ("DangerSoft", "DangerHover")),
        _ => IconPill("Inactive", null, ("NeutralSoft", "BodyText"))
    };

    /// <summary>A smaller clinic status pill (next to clinic names).</summary>
    public static View SmallClinicStatus(string status)
    {
        var (bg, fg) = status switch
        {
            Models.AccountStatus.Active => ("Mint", "Success"),
            Models.AccountStatus.Pending => ("WarningSoft", "Warning"),
            Models.AccountStatus.Rejected => ("DangerSoft", "DangerHover"),
            _ => ("NeutralSoft", "BodyText")
        };
        return new Border
        {
            Padding = new Thickness(7, 1),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            BackgroundColor = ThemeResources.Color(bg),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label { Text = status ?? "—", FontFamily = "DMSansBold", FontSize = 11, TextColor = ThemeResources.Color(fg) }
        };
    }

    /// <summary>Device health: Healthy mint, Stale amber, Failing light red, Never synced pale blue, Inactive gray.</summary>
    public static View DeviceHealth(string health) => health switch
    {
        Helpers.SyncHealthHelper.Healthy => IconPill("Healthy", "cloud-check", ("Mint", "Success")),
        Helpers.SyncHealthHelper.Stale => IconPill("Stale", "clock", ("WarningSoft", "Warning")),
        Helpers.SyncHealthHelper.Failing => IconPill("Failing", "cloud-off", ("DangerSoft", "DangerHover")),
        Helpers.SyncHealthHelper.NeverSynced => IconPill("Never synced", "circle-dashed", ("PaleBlue", "Navy")),
        _ => IconPill("Inactive", "ban", ("NeutralSoft", "BodyText"))
    };
}
