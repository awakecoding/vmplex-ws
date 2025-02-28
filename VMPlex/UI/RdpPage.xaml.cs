﻿/*
 * Copyright (c) 2022 Ira Strawser. All rights reserved.
 */

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using System.ComponentModel;

using HyperV;
using System.DirectoryServices.ActiveDirectory;

namespace VMPlex.UI
{
    /// <summary>
    /// Interaction logic for RdpPage.xaml
    /// </summary>
    public partial class RdpPage : UserControl, INotifyPropertyChanged
    {
        private readonly DispatcherTimer m_timer = new DispatcherTimer();
        public string ErrorMessage { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyChange(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public RdpPage(RdpSettings settings)
        {
            InitializeComponent();
            Connect(settings);
        }

        private RdpOptions MakeRdpOptions(RdpSettings settings)
        {
            var options = new RdpOptions();

            options.Domain = settings.Domain;
            options.Server = settings.Server;
            options.Port = 3389;
            options.EnhancedSession = settings.DefaultEnhancedSession;
            options.RedirectClipboard = settings.RedirectClipboard;
            options.AudioRedirectionMode = (uint)settings.AudioRedirectionMode;
            options.AudioCaptureRedirectionMode = settings.AudioCaptureRedirectionMode;
            options.RedirectDrives = settings.RedirectDrives;
            options.RedirectDevices = settings.RedirectDevices;
            options.RedirectSmartCards = settings.RedirectSmartCards;
            options.DesktopWidth = settings.DesktopWidth;
            options.DesktopHeight = settings.DesktopHeight;

            return options;
        }

        private void Connect(RdpSettings settings)
        {
            rdpHost.Height = rdp.Height;
            rdpHost.Width = rdp.Width;
            rdpGrid.SizeChanged += OnGridSizeChanged;
            rdpHost.DpiChanged += RdpHost_DpiChanged;
            rdp.DesktopResized += OnRdpDesktopResized;

            rdp.InitializeForRemoteDesktopConnection(MakeRdpOptions(settings));

            m_timer.Tick += OnResizeTimer;
            rdp.OnRdpConnecting += OnRdpConnecting;
            rdp.OnRdpConnected += OnRdpConnected;
            rdp.OnRdpDisconnected += OnRdpDisconnected;
            rdp.OnRdpError += OnRdpError;

            rdp.Connect();
        }

        private void DisplayErrorMessage(string message)
        {
            ErrorMessage = message;
            errorText.Visibility = Visibility.Visible;
            rdpHost.Visibility = Visibility.Hidden;
            NotifyChange("ErrorMessage");
        }

        private void HideErrorMessage()
        {
            ErrorMessage = "";
            errorText.Visibility = Visibility.Hidden;
            NotifyChange("ErrorMessage");
        }

        private void OnRdpConnecting(object sender)
        {
            this.Dispatcher.Invoke(() =>
            {
                connectingText.Visibility = Visibility.Visible;
                rdpHost.Visibility = Visibility.Hidden;
            });
        }

        private void OnRdpConnected(object sender)
        {
            System.Diagnostics.Debug.Print("Rdp connected");
            connectingText.Visibility = Visibility.Hidden;
            rdpHost.Visibility = System.Windows.Visibility.Visible;
            if (rdp.Enhanced)
            {
                StartResizeTimer();
            }
        }

        private void OnRdpDisconnected(object sender)
        {
            System.Diagnostics.Debug.Print("Rdp disconnected");
            this.Dispatcher.Invoke(() =>
            {
                connectingText.Visibility = Visibility.Hidden;
                rdpHost.Visibility = System.Windows.Visibility.Hidden;
                HideErrorMessage();
            });
            m_timer.Stop();
        }

        private void OnRdpError(object sender, RdpClient.RdpError error)
        {
            this.Dispatcher.Invoke(() =>
            {
                switch(error)
                {
                case RdpClient.RdpError.BasicSessionWithShieldedVm:
                    DisplayErrorMessage("Cannot connect to a shielded virtual machine with a basic session.\nPlease enable the Enhanced Mode option.");
                    break;
                }
            });
        }

        public void Shutdown()
        {
            rdp.Shutdown();
        }

        public void Disconnect()
        {
            System.Diagnostics.Debug.Print("rdp resize timer stopped in Disconnect");
            m_timer.Stop();
            try
            {
                rdp.Disconnect();
            }
            catch (Exception)
            {
            }
        }

        // Resize the WindowsFormsHost that contains the RDP ActiveX control taking into account available space
        // and DPI scaling. This uses the current DesktopSize from the ActiveX control.
        private void ResizeRdpHost(Size gridSize)
        {
            System.Windows.Size rdpSize = rdp.ScaledRdpDesktopSize();
            double targetWidth = rdpSize.Width;
            double targetHeight = rdpSize.Height;
            targetWidth = Math.Min(targetWidth, gridSize.Width);
            targetHeight = Math.Min(targetHeight, gridSize.Height);
            if (rdp.EnhancedReady)
            {
            }
            System.Diagnostics.Debug.Print("Resizing rdpHost to {0}, {1}", targetWidth, targetHeight);
            rdpHost.Width = targetWidth;
            rdpHost.Height = targetHeight;
        }

        // Fired when moving between monitors with different DPI scaling or if the monitor/system DPI scaling
        // settings are changed
        private void RdpHost_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            DpiScale dpi = e.NewDpi;

            Debug.Print("RdpHost_DpiChanged: DpiScaleX: {0} DpiScaleY: {1}",
                dpi.DpiScaleX, dpi.DpiScaleY);

            System.Diagnostics.Debug.Print("OnSizeChanged: RdpHost_DpiChanged");
            ResizeRdpHost(new Size(rdpGrid.ActualWidth, rdpGrid.ActualHeight));
        }

        // Fired when the ActiveX RDP control notifies us the remote desktop size has changed
        private void OnRdpDesktopResized(object sender, EventArgs e)
        {
            ResizeRdpHost(new Size(rdpGrid.ActualWidth, rdpGrid.ActualHeight));
        }

        // Fired when the grid containing the WindowsFormsHost (which then hosts the RDP ActiveX control) is resized.
        // We need to check if the WindowsFormsHost needs to be resized to fit within the new available space.
        private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.Print("Rdp grid resized {0}, {1}", e.NewSize.Width, e.NewSize.Height);
            if (!rdp.EnhancedReady)
            {
                ResizeRdpHost(new Size(e.NewSize.Width, e.NewSize.Height));
            }
            else
            {
                StartResizeTimer();
            }
        }

        private void OnResizeTimer(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.Print("Rdp resize timer {0} {1}", rdpGrid.ActualWidth, rdpGrid.ActualHeight);
            if (rdp.IsVideoAvailable() == false || rdp.TryResize(rdpGrid.ActualWidth, rdpGrid.ActualHeight))
            {
                rdpHost.Width = rdpGrid.ActualWidth;
                rdpHost.Height = rdpGrid.ActualHeight;
                m_timer.Stop();
                System.Diagnostics.Debug.Print("rdp resize timer stopped in OnResizeTimer");
                return;
            }
        }

        private void StartResizeTimer()
        {
            int seconds = 1;
            int milliseconds = 0;
            if (rdp.EnhancedReady)
            {
                seconds = 0;
                milliseconds = 200;
            }
            System.Diagnostics.Debug.Print("Starting rdp resize timer");
            m_timer.Stop();
            m_timer.Interval = new TimeSpan(0, 0, 0, seconds, milliseconds);
            m_timer.Start();
        }
    }
}
