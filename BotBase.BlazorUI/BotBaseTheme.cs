using MudBlazor;

namespace BotBase.BlazorUI;

// Фирменная тема: светлые страницы, фиолетовые акценты, шрифт Manrope (как в видео-инструкциях).
public static class BotBaseTheme
{
    static readonly string[] Font = ["Manrope", "Segoe UI", "Roboto", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#6D5DFC",
            PrimaryDarken = "#5847E8",
            Secondary = "#64748B",       // спокойный серый для подписей вместо розового
            Tertiary = "#A78BFA",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            Info = "#3B82F6",
            Background = "#F6F7FB",
            Surface = "#FFFFFF",
            AppbarBackground = "rgba(246,247,251,0.85)",
            AppbarText = "#0F172A",
            DrawerBackground = "#0E0B1F",
            DrawerText = "rgba(255,255,255,0.72)",
            DrawerIcon = "rgba(255,255,255,0.55)",
            TextPrimary = "#0F172A",
            TextSecondary = "#64748B",
            LinesDefault = "#E8EAF2",
            TableLines = "#EEF0F6",
            Divider = "#E8EAF2",
            ActionDefault = "#64748B",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Font, FontSize = ".9375rem", LetterSpacing = "0" },
            H4 = new H4Typography { FontFamily = Font, FontWeight = "800", FontSize = "1.85rem", LetterSpacing = "-.02em" },
            H5 = new H5Typography { FontFamily = Font, FontWeight = "800", LetterSpacing = "-.01em" },
            H6 = new H6Typography { FontFamily = Font, FontWeight = "700", FontSize = "1.1rem" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Font, FontWeight = "600" },
            Body1 = new Body1Typography { FontFamily = Font },
            Body2 = new Body2Typography { FontFamily = Font },
            Button = new ButtonTypography { FontFamily = Font, FontWeight = "700", TextTransform = "none", LetterSpacing = "0" },
            Caption = new CaptionTypography { FontFamily = Font },
            Overline = new OverlineTypography { FontFamily = Font, FontWeight = "700", LetterSpacing = ".08em" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "12px", DrawerWidthLeft = "264px" }
    };
}
