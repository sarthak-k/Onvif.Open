// Sarthak Killedar
// 2016-03-06 @ 12:43 PM

using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using Onvif.Open.Core.Abstract.Interface.Authenticator;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Core.Implementation
{
    public class PasswordDigestBehaviour : IEndpointBehavior
    {
        private readonly ICameraInformation _camera;

        #region Constructor

        private PasswordDigestBehaviour(ICameraInformation camera)
        {
            _camera = camera;
        }

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public void Validate(ServiceEndpoint endpoint)
        {
            
        }

        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
            
        }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {

        }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            clientRuntime.MessageInspectors.Add(new PasswordDigestMessageInspector(_camera.Username, _camera.Password));
        }

        internal static PasswordDigestBehaviour CreateInstance(ICameraInformation camera)
        {
            return new PasswordDigestBehaviour(camera);
        }
        
        #endregion

    }
}