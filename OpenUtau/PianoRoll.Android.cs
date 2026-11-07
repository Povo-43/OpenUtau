#if ANDROID
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Controls;

public partial class PianoRoll {
    private DispatcherTimer? _longPressTimer;
    private Pointer? _longPressPointer;
    private Point _longPressStartPosition;
    private DateTime? _longPressStartTime;
    private const int LongPressDelayMs = 600;
    private const double LongPressMoveThreshold = 10; // pixels

    // Pinch-to-zoom tracking
    private readonly Dictionary<int, Point> _touchPoints = new();
    private double _lastPinchDistance;
    private bool _pinching;

    static PianoRoll() {
        // Ensure touch devices are treated as left-button input on Android
        // (Avalonia.Android already maps touch to LeftButton by default)
    }

    partial void InitializeTouchSupport() {
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressDelayMs) };
        _longPressTimer.Tick += OnLongPressTick;

        // Hook pointer events for touch-specific handling
        this.AddHandler(PointerPressedEvent, OnTouchPointerPressed, RoutingStrategies.Tunnel);
        this.AddHandler(PointerMovedEvent, OnTouchPointerMoved, RoutingStrategies.Tunnel);
        this.AddHandler(PointerReleasedEvent, OnTouchPointerReleased, RoutingStrategies.Tunnel);
        this.AddHandler(PointerWheelChangedEvent, OnTouchPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void OnTouchPointerPressed(object? sender, PointerPressedEventArgs args) {
        if (args.Pointer.Type != PointerType.Touch) return;

        var pointer = args.Pointer as Pointer;
        if (pointer == null) return;

        var control = (Control)sender!;
        var point = args.GetCurrentPoint(control);

        // Start long-press timer
        _longPressPointer = pointer;
        _longPressStartPosition = point.Position;
        _longPressStartTime = DateTime.Now;
        _longPressTimer?.Start();

        // Track touch point for pinch detection
        _touchPoints[pointer.Id] = point.Position;

        // Pinch start: 2 fingers down
        if (_touchPoints.Count == 2) {
            _pinching = true;
            _lastPinchDistance = PinchDistance();
            _longPressTimer?.Stop();
            _longPressPointer = null;
        }
    }

    private void OnTouchPointerMoved(object? sender, PointerEventArgs args) {
        if (args.Pointer.Type != PointerType.Touch) return;

        var pointer = args.Pointer as Pointer;
        if (pointer == null) return;

        var control = (Control)sender!;
        var point = args.GetCurrentPoint(control);
        _touchPoints[pointer.Id] = point.Position;

        if (_pinching && _touchPoints.Count == 2) {
            var newDistance = PinchDistance();
            var scale = newDistance / _lastPinchDistance;
            if (Math.Abs(scale - 1.0) > 0.02) {
                var center = PinchCenter();
                var normalized = center.WithX(center.X / control.Bounds.Width)
                                        .WithY(center.Y / control.Bounds.Height);
                // Zoom both X and Y by the same factor
                var deltaY = Math.Log(scale) * 5.0; // sensitivity
                xZoom?.By(normalized, deltaY, true);
                yZoom?.By(normalized, deltaY, true);
                _lastPinchDistance = newDistance;
            }
            args.Handled = true;
            return;
        }

        // Long-press move threshold: cancel if finger moves too much
        if (_longPressTimer?.IsEnabled == true && _longPressPointer == pointer) {
            var delta = point.Position - _longPressStartPosition;
            if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) > LongPressMoveThreshold) {
                _longPressTimer.Stop();
                _longPressPointer = null;
            }
        }
    }

    private void OnTouchPointerReleased(object? sender, PointerReleasedEventArgs args) {
        if (args.Pointer.Type != PointerType.Touch) return;

        var pointer = args.Pointer as Pointer;
        if (pointer == null) return;

        _touchPoints.Remove(pointer.Id);

        if (_pinching) {
            _pinching = false;
            _lastPinchDistance = 0;
            args.Handled = true;
            return;
        }

        _longPressTimer?.Stop();
        _longPressPointer = null;
    }

    private void OnTouchPointerWheelChanged(object? sender, PointerWheelEventArgs args) {
        // On Android, wheel events from stylus buttons or scroll may arrive here.
        // Map to existing scroll/zoom behavior is handled by the base handlers.
    }

    private void OnLongPressTick(object? sender, EventArgs e) {
        _longPressTimer?.Stop();
        if (_longPressPointer == null) return;

        // Trigger context menu (right-click equivalent)
        shouldOpenNotesContextMenu = true;
        var contextMenu = this.Find<ContextMenu>("NotesContextMenu");
        if (contextMenu != null && _longPressPointer != null) {
            // Position the menu near the long-press point
            // ContextMenu.Open() positions relative to the control; we use a PlacementTarget-free approach
            contextMenu.Open();
        }
        _longPressPointer = null;
    }

    private double PinchDistance() {
        if (_touchPoints.Count != 2) return 0;
        var enumerator = _touchPoints.GetEnumerator();
        enumerator.MoveNext();
        var p1 = enumerator.Current.Value;
        enumerator.MoveNext();
        var p2 = enumerator.Current.Value;
        return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
    }

    private Point PinchCenter() {
        if (_touchPoints.Count != 2) return new Point(0, 0);
        var enumerator = _touchPoints.GetEnumerator();
        enumerator.MoveNext();
        var p1 = enumerator.Current.Value;
        enumerator.MoveNext();
        var p2 = enumerator.Current.Value;
        return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }
}
#endif
