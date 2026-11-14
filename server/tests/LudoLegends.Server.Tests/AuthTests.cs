using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoLegends.Server.Tests
{
    public class AuthServiceTests
    {
        private static (TestDatabase harness, AuthService auth, ServerOptions options) Setup()
        {
            var harness = new TestDatabase();
            ServerOptions options = TestOptions.Create();
            var google = new GoogleTokenValidator(string.Empty);
            var auth = new AuthService(harness.NewContext(), options, google, NullLogger<AuthService>.Instance);
            return (harness, auth, options);
        }

        [Fact]
        public async Task Guest_sign_in_is_stable_per_device()
        {
            var (harness, auth, _) = Setup();
            using (harness)
            {
                Account first = await auth.SignInGuestAsync("device-aaaaaaaa-1111", "Guest One", CancellationToken.None);
                Account second = await auth.SignInGuestAsync("device-aaaaaaaa-1111", "Guest One", CancellationToken.None);

                Assert.Equal(first.Id, second.Id);
                Assert.True(first.IsGuest);
                Assert.False(string.IsNullOrWhiteSpace(first.PlayerCode));
                Assert.Equal(6, first.PlayerCode.Length);

                using GameDbContext db = harness.NewContext();
                Assert.Equal(1, await db.Accounts.CountAsync());
            }
        }

        [Fact]
        public async Task Different_devices_get_different_accounts()
        {
            var (harness, auth, _) = Setup();
            using (harness)
            {
                Account a = await auth.SignInGuestAsync("device-bbbbbbbb-2222", null, CancellationToken.None);
                Account b = await auth.SignInGuestAsync("device-cccccccc-3333", null, CancellationToken.None);

                Assert.NotEqual(a.Id, b.Id);
                Assert.NotEqual(a.PlayerCode, b.PlayerCode);
            }
        }

        [Fact]
        public async Task Device_ids_are_hashed_and_never_stored_raw()
        {
            var (harness, auth, _) = Setup();
            using (harness)
            {
                string deviceId = "device-dddddddd-4444";
                Account account = await auth.SignInGuestAsync(deviceId, "Someone", CancellationToken.None);

                Assert.NotNull(account.DeviceHash);
                Assert.NotEqual(deviceId, account.DeviceHash);
                Assert.Equal(AuthService.HashDeviceId(deviceId), account.DeviceHash);
            }
        }

        [Fact]
        public async Task Session_token_round_trip_and_tamper_detection()
        {
            var (harness, auth, options) = Setup();
            using (harness)
            {
                Account account = await auth.SignInGuestAsync("device-eeeeeeee-5555", "Token Test", CancellationToken.None);
                string token = auth.IssueToken(account);

                var validator = new TokenValidator(options);
                string accountId = await validator.ValidateAccountIdAsync(token);
                Assert.Equal(account.Id, accountId);

                string tampered = token.Substring(0, token.Length - 2) + "AA";
                Assert.Null(await validator.ValidateAccountIdAsync(tampered));
                Assert.Null(await validator.ValidateAccountIdAsync(""));
                Assert.Null(await validator.ValidateAccountIdAsync("not.a.token"));
            }
        }

        [Fact]
        public async Task Token_signed_with_another_key_is_rejected()
        {
            var (harness, auth, options) = Setup();
            using (harness)
            {
                Account account = await auth.SignInGuestAsync("device-ffffffff-6666", "Token Test", CancellationToken.None);
                string token = auth.IssueToken(account);

                ServerOptions other = TestOptions.Create();
                other.TokenSigningKey = "a-completely-different-signing-key-9876543210";
                var validator = new TokenValidator(other);

                Assert.Null(await validator.ValidateAccountIdAsync(token));
            }
        }

        [Fact]
        public async Task Deleting_an_account_releases_the_device_and_anonymises_the_row()
        {
            var (harness, auth, _) = Setup();
            using (harness)
            {
                Account account = await auth.SignInGuestAsync("device-gggggggg-7777", "Delete Me", CancellationToken.None);
                string oldId = account.Id;

                Account loaded = await auth.GetAccountAsync(oldId, CancellationToken.None);
                await auth.DeleteAccountAsync(loaded, CancellationToken.None);

                Assert.Null(await auth.GetAccountAsync(oldId, CancellationToken.None));

                Account fresh = await auth.SignInGuestAsync("device-gggggggg-7777", "New Life", CancellationToken.None);
                Assert.NotEqual(oldId, fresh.Id);

                using GameDbContext db = harness.NewContext();
                Account row = await db.Accounts.SingleAsync(a => a.Id == oldId);
                Assert.NotNull(row.DeletedUtc);
                Assert.Null(row.DeviceHash);
                Assert.Null(row.GoogleSubject);
            }
        }

        [Theory]
        [InlineData("ab", null)]
        [InlineData("this-name-is-way-too-long-for-the-game", "this-name-is-way")]
        [InlineData("GoodName", "GoodName")]
        [InlineData("with spaces", "with spaces")]
        [InlineData("bad<script>", "badscript")]
        public void Display_names_are_normalised(string input, string expected)
        {
            string result = AuthService.NormalizeName(input);
            if (expected == null)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.Equal(expected, result);
            }
        }

        [Fact]
        public async Task Google_sign_in_reports_not_configured_instead_of_failing_silently()
        {
            var (harness, auth, _) = Setup();
            using (harness)
            {
                Assert.False(auth.GoogleConfigured);

                await Assert.ThrowsAsync<NotConfiguredException>(() =>
                    auth.SignInGoogleAsync("fake-token", null, CancellationToken.None));
            }
        }
    }
}
