using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Ui.Commands;
using Y360OutlookConnector.Ui.Extensions;

namespace Y360OutlookConnector.Ui.Models
{
    public class TelemostSettingsModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly ComponentContainer _componentContainer;
        private readonly AppointmentItem _appointment;

        public TelemostSettingsModel(ComponentContainer componentContainer, AppointmentItem appointment)
        {
            _componentContainer = componentContainer ?? throw new ArgumentNullException(nameof(componentContainer));
            _appointment = appointment ?? throw new ArgumentNullException(nameof(appointment));

            LogIn = new AsyncRelayCommand(p => 
            {
                Telemetry.Signal(Telemetry.TelemostSettingsWindowEvents, "login_button");

                if (_componentContainer.LoginController?.IsUserLoggedIn is true)
                {
                    return;
                }

                _componentContainer.StartLogin();
            });

            CreateOrUpdateMeeting = new AsyncRelayCommand(async p => 
            {
                Telemetry.Signal(Telemetry.TelemostSettingsWindowEvents, "update_or_create_button");

                if (_componentContainer.LoginController?.IsUserLoggedIn is false)
                {
                    return;
                }

                await _appointment.CreateOrUpdateMeetingAsync(IsMeetingInternal);
            });
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InvokePropertyChanged<T>(Expression<Func<T>> property)
        {
            var expression = (MemberExpression)property.Body;
            OnPropertyChanged(expression.Member.Name);
        }

        private bool _isLoggedIn;


        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set
            {
                if (_isLoggedIn == value)
                {
                    return;
                }

                _isLoggedIn = value;
                InvokePropertyChanged( () => IsLoggedIn);
            }
        }

        private bool _isMeetingInternal;

        public bool IsMeetingInternal
        {
            get => _isMeetingInternal;
            set
            {
                if (_isMeetingInternal == value)
                {
                    return;
                }

                _isMeetingInternal = value;
                InvokePropertyChanged(() => IsMeetingInternal);
            }
        }

        private bool _isMeetingCreated;

        public bool IsMeetingCreated
        {
            get => _isMeetingCreated;
            set
            {
                if (_isMeetingCreated == value)
                {
                    return;
                }

                _isMeetingCreated = value;
                InvokePropertyChanged(() => IsMeetingCreated);
            }
        }

        private string _userName;
        public string UserName
        {
            get => _userName;
            set
            {
                if (_userName == value)
                {
                    return;
                }

                _userName = value;
                InvokePropertyChanged(() => UserName);
            }
        }

        private string _userEmail;
        public string UserEmail
        {
            get => _userEmail;
            set
            {
                if (_userEmail == value)
                {
                    return;
                }

                _userEmail = value;
                InvokePropertyChanged(() => UserEmail);
            }
        }

        private BitmapSource _userAvatar;
        public BitmapSource UserAvatar
        {
            get => _userAvatar;

            set
            {
                _userAvatar = value;
                InvokePropertyChanged(() => UserAvatar);
            }
        }


        public ICommand LogIn { get; private set; }
        public ICommand CreateOrUpdateMeeting { get; private set; }
    }
}
