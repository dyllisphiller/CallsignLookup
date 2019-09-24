﻿using Hammer.Callsigns;
using Hammer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Services.Maps;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;

namespace Hammer.Views
{
    /// <summary>
    /// The callsign SERP, meant to be navigated to within a Frame.
    /// </summary>
    public sealed partial class SearchPage : Page
    {
        public LicenseViewModel ViewModel { get; set; }

        public SearchPage()
        {
            this.InitializeComponent();
            this.ViewModel = new LicenseViewModel();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is string && !string.IsNullOrWhiteSpace((string)e.Parameter))
            {
                await CallsignSearch((string)e.Parameter).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// The base address for the callsign lookup endpoint with a trailing slash.
        /// </summary>
        // callook.info is pretty awesome with their API terms
        // If anyone there is reading this: thanks!
        const string EndpointURL = "https://callook.info/";

        private License LicenseSearchResult = new License();
        Geopoint licensePlacecardGeopoint;


        public async Task RetrieveData(string callsign)
        {
            // Initialize variables
            JObject jResult;
            string result;

            string ErrorMessage = "";
            string CallsignUpper = callsign.ToUpperInvariant();

            // TODO: Reset result fields
            // LookupResultField.Text = "";

            // assemble the address
            Uri.TryCreate($"{EndpointURL}{CallsignUpper}/json", UriKind.Absolute, out Uri requestUri);

            using (var client = new HttpClient())
            {
                try
                {
                    // Make it clear we really do want JSON.
                    // This might be omitted; API requests ending in /json always return JSON
                    client.DefaultRequestHeaders.Accept.TryParseAdd("application/json");
                    HttpResponseMessage httpResponse = await client.GetAsync(requestUri);
                    httpResponse.EnsureSuccessStatusCode();
                    result = await httpResponse.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    throw;
                }
            }

            try
            {
                jResult = JObject.Parse(result);
                LicenseSearchResult.TryParse(jResult, out LicenseSearchResult);

                switch (LicenseSearchResult.Status)
                {
                    case "VALID":
                        // Display the results in their fields
                        SearchResultPivot.Title = $"{CallsignUpper}: {LicenseSearchResult.Name}";
                        //RegistrantTypeField.Text = LicenseSearchResult.Type;

                        AddressAttnField.Text = 
                            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                                LicenseSearchResult.AddressAttn.ToLower(CultureInfo.CurrentCulture)
                            );
                        AddressLine1Field.Text =
                            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                                LicenseSearchResult.AddressLine1.ToLower(CultureInfo.CurrentCulture)
                            );
                        AddressLine2Field.Text =
                            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                                LicenseSearchResult.AddressLine2.ToLower(CultureInfo.CurrentCulture)
                            );

                        LocationCoordinatesField.Text = LicenseSearchResult.Location.Coordinates;
                        GridSquareField.Text = LicenseSearchResult.GridSquare;

                        DateGrantedField.Text = LicenseSearchResult.GrantDate.ToString("d");
                        DateExpiryField.Text = LicenseSearchResult.ExpiryDate.ToString("d");
                        DateLastActionField.Text = LicenseSearchResult.LastActionDate.ToString("d");

                        //UlsUriField.Text = LicenseSearchResult.UlsUri.ToString();

                        licensePlacecardGeopoint = new Geopoint(new BasicGeoposition { Latitude = LicenseSearchResult.Location.Latitude, Longitude = LicenseSearchResult.Location.Longitude });

                        break;

                    case "UPDATING":
                        throw new Exception(message: "The data source is getting the latest license data from the FCC. This might take a bit. Please try again later.");

                    default:
                        throw new Exception(message: "Either the callsign is invalid or something unexpected happened. Try again?");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"{ex.Message}";
            }

            if (!String.IsNullOrEmpty(ErrorMessage))
            {
                System.Diagnostics.Debug.WriteLine(ErrorMessage);
            }
        }

        private async Task CallsignSearch(string callsign)
        {
            //Contract.Requires<ArgumentNullException>(callsign != null, "callsign");
            Regex _rgx = new Regex(@"[0-z]{3,12}");

            if (String.IsNullOrEmpty(callsign))
            {
                throw new ApplicationException("Callsign field must not be empty.");
            }
            else if (!_rgx.IsMatch(callsign))
            {
                string exception = $"'{callsign.ToUpperInvariant()}' does not appear to be a valid callsign.";
                throw new ApplicationException(exception);
            }
            else if (Parser.IsCallsignIssuedByUnitedStates(callsign))
            {
                // Spinny blade wall! Actually it's just a spinner.
                // Can we call it a fidget spinner? Can computers fidget?
                // This needs to be a thing for generalized AI. Occasional fidgeting.
                // Imagine it pinging a random host for funsies. Toggling the power light.
                // Or, if the machine is old, opening and closing the optical drive tray.
                SearchResultsStackPanel.Visibility = Visibility.Collapsed;
                SearchProgressRing.IsActive = true;
                SearchProgressRing.Visibility = Visibility.Visible;

                await RetrieveData(callsign).ConfigureAwait(true);

                // All done. Nothing to see here.
                SearchProgressRing.Visibility = Visibility.Collapsed;
                SearchProgressRing.IsActive = false;
                SearchResultsStackPanel.Visibility = Visibility.Visible;

            }
        }

        private void ShowMapButton_Click(object sender, RoutedEventArgs e)
        {
            PlaceInfoCreateOptions options = new PlaceInfoCreateOptions
            {
                DisplayAddress = $"{LicenseSearchResult.AddressLine1}, {LicenseSearchResult.AddressLine2}",
                DisplayName = $"{LicenseSearchResult.Callsign}"
            };

            PlaceInfo licensePlaceInfo = PlaceInfo.Create(licensePlacecardGeopoint, options);

            FrameworkElement targetElement = (FrameworkElement)sender;

            GeneralTransform generalTransform =
                targetElement.TransformToVisual((FrameworkElement)targetElement.Parent);

            Rect rectangle = generalTransform.TransformBounds(new Rect(new Point
                (targetElement.Margin.Left, targetElement.Margin.Top), targetElement.RenderSize));

            licensePlaceInfo.Show(rectangle, Windows.UI.Popups.Placement.Below);
        }

        private async void UlsUriButton_Click(object sender, RoutedEventArgs e)
        {
            //var options = new Windows.System.LauncherOptions();
            //options.TreatAsUntrusted = true;

            //await Launcher.LaunchUriAsync(LicenseSearchResult.UlsUri, options);
            await Launcher.LaunchUriAsync(LicenseSearchResult.UlsUri);
        }

        private async void SearchTrusteeButton_Click(object sender, RoutedEventArgs e)
        {
            // Null reference exception.
            //await CallsignSearch(LicenseSearchResult.Trustee.Callsign).ConfigureAwait(true);
        }
    }
}
