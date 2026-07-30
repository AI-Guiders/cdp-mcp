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
        FrameView? p = null, f = null, m = null;
        var root = BuildRoot(fixture, seatFilter, out var refresh, ref p, ref f, ref m);

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key.IsCtrl || key.IsAlt)
                return;

            if (key == Key.R)
            {
                refresh(DeskFixture.Sample());
                key.Handled = true;
                return;
            }

            // 0 = un-Pascal the layout after playful hands
            if (key == Key.D0 || key == Key.Home)
            {
                if (p is not null && f is not null && m is not null)
                {
                    ApplyEqualColumns(p, f, m);
                    root.SetNeedsDraw();
                }

                key.Handled = true;
            }
        };

        try
        {
            Application.DefaultKeyBindings[Command.Quit] = Bind.All(Key.Q.WithCtrl, Key.Q);
        }
        catch
        {
            /* best-effort */
        }

        app.Run(root);
        root.Dispose();
        return 0;
    }

    static Runnable BuildRoot(
        DeskFixture initial,
        string? seatFilter,
        out Action<DeskFixture> refresh,
        ref FrameView? pSeat,
        ref FrameView? fSeat,
        ref FrameView? mSeat)
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
            pSeat = MakeSeat("P · plan", pText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(pSeat, status);
        }
        else if (filter is "f" or "forward" or "editor")
        {
            fSeat = MakeSeat("F · editor", fText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(fSeat, status);
        }
        else if (filter is "m" or "shell")
        {
            mSeat = MakeSeat("M · shell", mText, 0, 0, Dim.Fill(), Dim.Fill(1));
            top.Add(mSeat, status);
        }
        else
        {
            pSeat = MakeSeat("P · plan", pText, 0, 0, Dim.Percent(33), Dim.Fill(1));
            fSeat = MakeSeat("F · editor", fText, Pos.Percent(33), 0, Dim.Percent(34), Dim.Fill(1));
            mSeat = MakeSeat("M · shell", mText, Pos.Percent(67), 0, Dim.Fill(), Dim.Fill(1));

            // Drag borders OK — if a seat vanishes, press 0 / Home to reset.
            fSeat.Arrangement = ViewArrangement.LeftResizable | ViewArrangement.RightResizable;
            fSeat.Border!.Thickness = new Thickness(1, 0, 1, 0);
            fSeat.SuperViewRendersLineCanvas = true;

            mSeat.Arrangement = ViewArrangement.LeftResizable;
            mSeat.Border!.Thickness = new Thickness(1, 0, 0, 0);
            mSeat.SuperViewRendersLineCanvas = true;

            top.Add(pSeat, fSeat, mSeat, status);
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

    static void ApplyEqualColumns(FrameView p, FrameView f, FrameView m)
    {
        p.X = 0;
        p.Width = Dim.Percent(33);
        f.X = Pos.Percent(33);
        f.Width = Dim.Percent(34);
        m.X = Pos.Percent(67);
        m.Width = Dim.Fill();
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
        $"{d.Banner}  ·  {d.Alert}  ·  {d.AtUtc:HH:mm:ss}Z  ·  {d.Hint}  ·  0=reset seats";
}
