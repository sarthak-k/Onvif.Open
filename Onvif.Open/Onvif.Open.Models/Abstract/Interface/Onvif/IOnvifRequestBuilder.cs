// Sarthak Killedar
// 2016-03-06 @ 9:57 AM

using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Implementation;

namespace Onvif.Open.Core.Abstract.Interface.Onvif
{
    public interface IOnvifRequestBuilder<in T, out TInformation>
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

        TInformation Build();
        IOnvifRequestBuilder<T, TInformation> SetProperties(T obj);
        IOnvifRequestBuilder<T, TInformation> SetCamera(ICameraInformation camera);
        IOnvifRequestBuilder<T, TInformation> SetEndpoint(IOnvifEndpoint endpoint);
        IOnvifRequestBuilder<T, TInformation> SetEndpoint();
        IOnvifRequestBuilder<T, TInformation> SetAuthentication(IEndpointBehavior authenticator);
        IOnvifRequestBuilder<T, TInformation> SetClient();

        #endregion

    }
}