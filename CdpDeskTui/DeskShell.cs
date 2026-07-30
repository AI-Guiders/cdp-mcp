#nullable enable
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CdpDeskTui;

/// <summary>P|F|M tiled projector — anti-chrome desk feel.</summary>
internal static class DeskShell
{
    public static int Run(string? seatFilter)
    {
        using IApplication app = Application.Create();
        app.Init();

        var fixture = DeskFixture.Sample();
        var root = BuildRoot(fixture, seatFilter, out var refresh);

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key == Key.R && !key.IsCtrl && !key.IsAlt)
            {
                refresh(DeskFixture.Sample());
                key.Handled = true;
            }
        };

        try
        {
            Application.DefaultKeyBindings[Command.Quit] = Bind.All(Key.Q.WithCtrl, Key.Q);
        }
        catch
        {
            /* best-effort — Esc still quits in many TG defaults */
        }

        app.Run(root);
        root.Dispose();
        return 0;
    }

    static Runnable BuildRoot(
        DeskFixture initial,
        string? seatFilter,
        out Action<DeskFixture> refresh)
    {
        var top = new Runnable
        {
            Title = "cdp-desk-tui",
            BorderStyle = LineStyle.None
        };

        var status = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = FormatStatus(initial)
        };

        TextView pText = MakeBody(initial.Plan);
        TextView fText = MakeBody(initial.Forward);
        TextView mText = MakeBody(initial.M);

        var filter = (seatFilter ?? "all").Trim().ToLowerInvariant();
        if (filter is "p" or "plan")
        {
            var p = MakeSeat("P · plan", pText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(p, status);
        }
        else if (filter is "f" or "forward" or "editor")
        {
            var f = MakeSeat("F · editor", fText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(f, status);
        }
        else if (filter is "m" or "shell")
        {
            var m = MakeSeat("M · shell", mText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(m, status);
        }
        else
        {
            var p = MakeSeat("P · plan", pText, 0, 0, Dim.Percent(33), Dim.Fill(1));
            var f = MakeSeat("F · editor", fText, Pos.Right(p) - 1, 0, Dim.Percent(34), Dim.Fill(1));
            var m = MakeSeat("M · shell", mText, Pos.Right(f) - 1, 0, Dim.Fill(), Dim.Fill(1));

            f.Arrangement = ViewArrangement.LeftResizable | ViewArrangement.RightResizable;
            f.Border!.Thickness = new Thickness(1, 0, 1, 0);
            f.SuperViewRendersLineCanvas = true;

            m.Arrangement = ViewArrangement.LeftResizable;
            m.Border!.Thickness = new Thickness(1, 0, 0, 0);
            m.SuperViewRendersLineCanvas = true;

            p.Width = Dim.Fill(Dim.Func(_ => f.Frame.Width + m.Frame.Width));

            top.Add(p, f, m, status);
        }

        refresh = next =>
        {
            pText.Text = next.Plan;
            fText.Text = next.Forward;
            mText.Text = next.M;
            status.Text = FormatStatus(next);
            top.SetNeedsDraw();
        };

        return top;
    }

    static FrameView MakeSeat(string title, View body, Pos x, Pos y, Dim w, Dim h)
    {
        var frame = new FrameView
        {
            Title = title,
            X = x,
            Y = y,
            Width = w,
            Height = h,
            BorderStyle = LineStyle.Single
        };
        body.X = 0;
        body.Y = 0;
        body.Width = Dim.Fill();
        body.Height = Dim.Fill();
        frame.Add(body);
        return frame;
    }

    static TextView MakeBody(string text) => new()
    {
        Text = text,
        ReadOnly = true,
        WordWrap = true
    };

    static string FormatStatus(DeskFixture d) =>
        $"{d.Banner}  ·  {d.Alert}  ·  {d.AtUtc:HH:mm:ss}Z  ·  {d.Hint}";
}
