using System;
using System.Windows;

namespace WeatherLauncher
{
    public sealed class App : Application
    {
        private readonly string[] args;

        public App(string[] args)
        {
            this.args = args ?? new string[0];
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        public string[] Args
        {
            get { return args; }
        }
    }
}
