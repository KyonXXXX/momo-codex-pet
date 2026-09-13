using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Momo;

public partial class PetWindow
{
    private void TestAutoCare(Action<bool, string> check, string output)
    {
        var oldLife = _settings.Life;
        bool oldEnabled = _settings.AutoCareEnabled;
        var oldCare = _autoCare;
        string oldMessage = _lastAutoCare;
        try
        {
            _settings.Life = new PetLife { Hunger = 40, Coins = 1000 };
            _settings.AutoCareEnabled = true; _autoCare = new();
            var selected = AutoCare.Select(_settings.Life, _catalog.Foods)!;
            StartActivity(_catalog.Activities.First(a => a.Type == "Work"));
            var work = _activity; int generation = _actionGeneration; string animation = _animation.Current;
            TryAutoCare(0);
            check(_settings.Life.Hunger >= 60 && Math.Abs(_settings.Life.Coins - (1000 - selected.Food.Price)) < .001, "automatic care buys a real shop item and restores stats at its exact price");
            check(_activity == work && _actionGeneration == generation && _animation.Current == animation, "automatic care preserves current work and animation");
            double paid = _settings.Life.Coins; _settings.Life.Thirst = 20;
            TryAutoCare(1); check(_settings.Life.Coins == paid, "repeated runtime ticks cannot buy twice within thirty seconds");
            StopPetActivity(); _focusEnd = DateTimeOffset.UtcNow.AddMinutes(25); var focus = _focusEnd;
            TryAutoCare(30000);
            check(_settings.Life.Thirst >= 60 && _focusEnd == focus, "automatic care continues during focus without cancelling its timer");
            _focusEnd = null; _settings.Life.Hunger = 40; paid = _settings.Life.Coins;
            FeedPet(_catalog.Foods.First(f => f.Price > 0 && f.Price < paid));
            TryAutoCare(60000); check(_settings.Life.Coins == paid && _actionFamily == "food", "pending manual feeding reserves its opportunity before automatic spending");
            StopPetActivity(); check(_settings.Life.Coins == paid, "cancelling manual feeding still costs no coins");
            _settings.AutoCareEnabled = false; TryAutoCare(60000);
            check(_settings.Life.Coins == paid, "disabled automatic care never buys an item");
            ShowInteractionPanel(); _interactionPanel!.UpdateLayout();
            static CheckBox? Find(DependencyObject root)
            {
                if (root is CheckBox box && Equals(box.Content, "自动购买补充（<60）")) return box;
                foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                    if (Find(child) is { } found) return found;
                return null;
            }
            var toggle = Find(_interactionPanel);
            check(toggle is not null && toggle.IsChecked == false, "interaction panel exposes the saved automatic care switch");
            toggle!.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            check(_settings.AutoCareEnabled, "automatic care checkbox enables the actual purchase setting");
            TryAutoCare(60000); check(_settings.Life.Coins < paid && _settings.Life.Hunger >= 60, "re-enabled automatic care resumes its purchase path");
            _interactionPanel.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)_interactionPanel.ActualWidth, (int)_interactionPanel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_interactionPanel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, "auto-care-panel.png")); encoder.Save(file);
        }
        finally
        {
            _interactionPanel?.Close(); StopPetActivity();
            _settings.Life = oldLife; _settings.AutoCareEnabled = oldEnabled; _autoCare = oldCare; _lastAutoCare = oldMessage;
            _lastLevel = oldLife.Level; _lastMood = Mood;
        }
    }
}
