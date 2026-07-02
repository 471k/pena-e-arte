namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Signs the OAuth `state` parameter carrying the artistId through the Instagram
/// redirect so the anonymous callback endpoint can trust it came from a connect-url
/// this API generated, rather than an attacker-supplied artistId (which would let
/// anyone link their own Instagram to any artist's public portfolio).
/// </summary>
public interface IInstagramStateSigner
{
    string Sign(Guid artistId);
    bool TryValidate(string state, out Guid artistId);
}
