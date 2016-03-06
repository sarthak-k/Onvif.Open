// Sarthak Killedar
// 2016-03-06 @ 9:57 AM

using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Implementation;

namespace Onvif.Open.Core.Abstract.Interface.Onvif
{
    public interface IOnvifRequestBuilder<T>
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
        IOnvifRequestBuilder<T> SetProperties(T obj);
        IOnvifRequestBuilder<T> SetCamera(ICameraInformation camera);
        IOnvifRequestBuilder<T> SetEndpoint(IOnvifEndpoint endpoint);
        IOnvifRequestBuilder<T> SetEndpoint();
        IOnvifRequestBuilder<T> SetAuthentication(IEndpointBehavior authenticator);
        IOnvifRequestBuilder<T> SetClient();

        #endregion

    }
}