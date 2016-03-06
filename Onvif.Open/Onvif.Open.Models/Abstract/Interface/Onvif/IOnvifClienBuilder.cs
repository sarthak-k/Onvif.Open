// Sarthak Killedar
// 2016-03-06 @ 9:55 PM

using System.ServiceModel;
using System.ServiceModel.Description;

namespace Onvif.Open.Core.Abstract.Interface.Onvif
{
    public interface IOnvifClienBuilder<T> where T : class
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        T Build();
        IOnvifClienBuilder<T> SetHttpTransportBinding();
        IOnvifClienBuilder<T> SetMessageElement();
        IOnvifClienBuilder<T> SetCustomeBinding();
        IOnvifClienBuilder<T> SetClient();
        IOnvifClienBuilder<T> SetEndpoint(EndpointAddress endpointAddress);
        IOnvifClienBuilder<T> SetAuthenticator(IEndpointBehavior behavior);

        #endregion
    }
}