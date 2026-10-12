using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FanOMax.App.Tray;

/// <summary>État résumé dans la zone de notification, du moins grave au plus grave.</summary>
public enum TrayState
{
    /// <summary>FanOMax pilote les ventilateurs.</summary>
    Piloting,

    /// <summary>Mode fantôme : le service calcule sans écrire.</summary>
    Shadow,

    /// <summary>À regarder : écriture bloquée, incident dans la carte « Stabilité ».</summary>
    Warning,

    /// <summary>Service arrêté, capteur perdu, température critique, ventilateurs rendus au BIOS.</summary>
    Alert,
}

/// <summary>
/// Icônes de la zone de notification, dessinées au lancement : un ventilateur blanc sur un disque de la couleur de l'état
/// (mêmes couleurs que les pastilles du tableau de bord). Pas de fichiers d'icône à maintenir.
/// </summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    public static WindowIcon Render(TrayState state)
    {
        var background = new SolidColorBrush(state switch
        {
            TrayState.Piloting => Color.Parse("#2FA866"),
            TrayState.Shadow => Color.Parse("#6B7383"),
            TrayState.Warning => Color.Parse("#E0A020"),
            _ => Color.Parse("#E5334B"),
        });

        using var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var center = new Point(Size / 2.0, Size / 2.0);
            context.DrawEllipse(background, null, center, Size / 2.0 - 0.5, Size / 2.0 - 0.5);

            // Trois pales, inclinées pour l'effet « hélice ».
            for (var i = 0; i < 3; i++)
            {
                var rotation = Matrix.CreateTranslation(-center.X, -center.Y)
                               * Matrix.CreateRotation((i * 120 + 15) * Math.PI / 180)
                               * Matrix.CreateTranslation(center.X, center.Y);
                using (context.PushTransform(rotation))
                {
                    context.DrawEllipse(Brushes.White, null, new Point(center.X, center.Y - 6.5), 4.5, 6.8);
                }
            }

            context.DrawEllipse(background, null, center, 2.6, 2.6);
        }

        // Passage par un PNG : l'icône Windows est construite depuis un flux, indépendamment du rendu.
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
