﻿using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System;
using System.Windows;

namespace NewFilterBoard.CommUnit
{
    public class CCPopup : Popup
    {
        public static DependencyProperty TopmostProperty = Window.TopmostProperty.AddOwner(typeof(CCPopup), new FrameworkPropertyMetadata(false, OnTopmostChanged));
        public bool Topmost
        {
            get { return (bool)GetValue(TopmostProperty); }
            set { SetValue(TopmostProperty, value); }
        }
        private static void OnTopmostChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            (obj as CCPopup).UpdateWindow();
        }
        protected override void OnOpened(EventArgs e)
        {
            UpdateWindow();
        }
        private void UpdateWindow()
        {
            var hwnd = ((HwndSource)PresentationSource.FromVisual(this.Child)).Handle;
            RECT rect;
            if (GetWindowRect(hwnd, out rect))
            {
                SetWindowPos(hwnd, Topmost ? -1 : -2, rect.Left, rect.Top, (int)this.Width, (int)this.Height, 0);
            }
        }
        #region P/Invoke imports & definitions
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32", EntryPoint = "SetWindowPos")]
        private static extern int SetWindowPos(IntPtr hWnd, int hwndInsertAfter, int x, int y, int cx, int cy, int wFlags);
        #endregion
    }
}
