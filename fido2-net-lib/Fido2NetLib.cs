﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Fido2NetLib.Objects;

namespace Fido2NetLib
{
    /// <summary>
    /// Public API for parsing and veriyfing FIDO2 attestation & assertion responses.
    /// </summary>
    public class Fido2
    {
        public class Configuration
        {
            public uint Timeout { get; set; } = 60000;
            public int ChallengeSize { get; set; } = 64;
            public string ServerDomain { get; set; }
            public string ServerName { get; set; }
            public string ServerIcon { get; set; }
            public string Origin { get; set; }
        }

        private Configuration Config { get; }

        private RandomNumberGenerator _crypto;

        public Fido2(Configuration config)
        {
            Config = config;
            _crypto = RandomNumberGenerator.Create();

        }

        /// <summary>
        /// Returns CredentialCreateOptions including a challenge to be sent to the browser/authr to create new credentials
        /// </summary>
        /// <returns></returns>
        /// <param name="excludeCredentials">Recommended. This member is intended for use by Relying Parties that wish to limit the creation of multiple credentials for the same account on a single authenticator.The client is requested to return an error if the new credential would be created on an authenticator that also contains one of the credentials enumerated in this parameter.</param>
        public CredentialCreateOptions RequestNewCredential(User user, AuthenticatorSelection authenticatorSelection, List<PublicKeyCredentialDescriptor> excludeCredentials)
        {
            return RequestNewCredential(user, authenticatorSelection, excludeCredentials, AttestationConveyancePreference.None);
        }

        /// <summary>
        /// Returns CredentialCreateOptions including a challenge to be sent to the browser/authr to create new credentials
        /// </summary>
        /// <returns></returns>
        /// <param name="attestationPreference">This member is intended for use by Relying Parties that wish to express their preference for attestation conveyance. The default is none.</param>
        /// <param name="excludeCredentials">Recommended. This member is intended for use by Relying Parties that wish to limit the creation of multiple credentials for the same account on a single authenticator.The client is requested to return an error if the new credential would be created on an authenticator that also contains one of the credentials enumerated in this parameter.</param>
        public CredentialCreateOptions RequestNewCredential(User user, AuthenticatorSelection authenticatorSelection, List<PublicKeyCredentialDescriptor> excludeCredentials, AttestationConveyancePreference attestationPreference)
        {
            // https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptions
            // challenge.rp
            // challenge.user
            // challenge.excludeCredentials
            // challenge.authenticatorSelection
            // challenge.attestation
            // challenge.extensions

            // note: I have no idea if this crypto is ok...
            var challenge = new byte[Config.ChallengeSize];
            _crypto.GetBytes(challenge);

            var options = CredentialCreateOptions.Create(challenge, Config, authenticatorSelection);
            options.User = user;
            options.Attestation = attestationPreference;
            if (excludeCredentials != null)
            {
                options.ExcludeCredentials = excludeCredentials;
            }

            return options;
        }

        /// <summary>
        /// Verifies the response from the browser/authr after creating new credentials
        /// </summary>
        /// <param name="attestionResponse"></param>
        /// <param name="origChallenge"></param>
        /// <returns></returns>
        public CredentialMakeResult MakeNewCredential(AuthenticatorAttestationRawResponse attestionResponse, CredentialCreateOptions origChallenge, IsCredentialIdUniqueToUserDelegate isCredentialIdUniqueToUser, byte[] requestTokenBindingId = null)
        {
            var parsedResponse = AuthenticatorAttestationResponse.Parse(attestionResponse);
            //Func<byte[], User, bool> isCredentialIdUniqueToUser = isCredentialIdUniqueToUser
            // add overload/null check and user config then maybe?
            var res = parsedResponse.Verify(origChallenge, Config.Origin, isCredentialIdUniqueToUser, requestTokenBindingId);


            var pk = BitConverter.ToString(res.PublicKey);
            var cid = BitConverter.ToString(res.CredentialId);

            // todo: Set Errormessage etc.
            return new CredentialMakeResult { Status = "ok", ErrorMessage = "", Result = res };
        }

        /// <summary>
        /// Returns AssertionOptions including a challenge to the browser/authr to assert existing credentials and authenticate a user.
        /// </summary>
        /// <returns></returns>
        public AssertionOptions GetAssertionOptions(IEnumerable<PublicKeyCredentialDescriptor> allowedCredentials, UserVerificationRequirement userVerification)
        {
            var challenge = new byte[Config.ChallengeSize];
            _crypto.GetBytes(challenge);

            var options = AssertionOptions.Create(Config, challenge, allowedCredentials, userVerification);
            return options;
        }

        /// <summary>
        /// Verifies the assertion response from the browser/authr to assert existing credentials and authenticate a user.
        /// </summary>
        /// <returns></returns>
        public AssertionVerificationSuccess MakeAssertion(AuthenticatorAssertionRawResponse assertionResponse, AssertionOptions originalOptions, byte[] storedPublicKey, uint storedSignatureCounter, IsUserHandleOwnerOfCredentialId isUserHandleOwnerOfCredentialIdCallback, byte[] requestTokenBindingId = null)
        {
            var parsedResponse = AuthenticatorAssertionResponse.Parse(assertionResponse);

            var result = parsedResponse.Verify(originalOptions, Config.Origin, storedPublicKey, storedSignatureCounter, isUserHandleOwnerOfCredentialIdCallback, requestTokenBindingId);

            return result;
        }

        /// <summary>
        /// Result of parsing and verifying attestation. Used to transport Public Key back to RP
        /// </summary>
        public class CredentialMakeResult
        {
            public string Status { get; set; }
            public string ErrorMessage { get; set; }
            public AttestationVerificationData Result { get; internal set; }

            // todo: add debuginfo?
        }
    }

    /// <summary>
    /// Paramters used for callback function
    /// </summary>
    public class CredentialIdUserParams
    {
        public byte[] CredentialId { get; set; }
        public User User { get; set; }

        public CredentialIdUserParams(byte[] credentialId, User user)
        {
            CredentialId = credentialId;
            User = user;
        }
    }

    /// <summary>
    /// Paramters used for callback function
    /// </summary>
    public class CredentialIdUserHandleParams
    {
        public byte[] UserHandle { get; }
        public byte[] CredentialId { get; }

        public CredentialIdUserHandleParams(byte[] credentialId, byte[] userHandle)
        {
            CredentialId = credentialId;
            UserHandle = userHandle;
        }
    }
    /// <summary>
    /// Paramters used for callback function
    /// </summary>
    public class StoreSignaturecounterParams
    {
        public byte[] CredentialID { get; }
        public uint SignatureCounter { get; }

        public StoreSignaturecounterParams(byte[] credentialID, uint signatureCounter)
        {
            CredentialID = credentialID;
            SignatureCounter = signatureCounter;
        }
    }

    /// <summary>
    /// Callback function used to validate that the CredentialID is unique to this User
    /// </summary>
    /// <param name="credentialIdUserParams"></param>
    /// <returns></returns>
    public delegate bool IsCredentialIdUniqueToUserDelegate(CredentialIdUserParams credentialIdUserParams);
    /// <summary>
    /// Callback function used to validate that the Userhandle is indeed owned of the CrendetialId
    /// </summary>
    /// <param name="credentialIdUserHandleParams"></param>
    /// <returns></returns>
    public delegate bool IsUserHandleOwnerOfCredentialId(CredentialIdUserHandleParams credentialIdUserHandleParams);
}
