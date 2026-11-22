using Microsoft.Extensions.Configuration;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// The Postgres password is fixed in appsettings.Development.json ("Parameters:postgres-password")
// rather than left to Aspire's default, which generates a random one and stores it in user
// secrets. Postgres bakes the password into the cluster at first start, and the data volume
// below outlives the user secrets: a Codespace rebuild (or a fresh clone on the same machine)
// wipes ~/.microsoft/usersecrets while docker-in-docker keeps /var/lib/docker, so the next run
// would hand the old cluster a brand-new password and fail with "password authentication failed
// for user postgres". A committed value is fine here: Development only, local container only.
// Lab 6 publishes without that file, so the deployed Postgres still gets its own password.
var postgres = builder
    .AddPostgres("postgres")
    .WithPgAdmin();

// The data volume is local only. Published, Aspire would back it with an Azure Files share,
// and Postgres refuses to initialise a cluster there: initdb has to chmod its data directory
// and SMB says "Operation not permitted", so the container crash-loops before the Host can
// connect. The deployed database is ephemeral instead -- the Host migrates and seeds it on
// every start, which is all this workshop needs.
if (builder.ExecutionContext.IsRunMode)
{
    postgres.WithDataVolume("travelbooking-pgdata");
}

var travelBookingDb = postgres.AddDatabase("travelbookingdb");

// Lab 6 publishes this graph to Azure Container Apps, where every project sits behind an https
// ingress; locally the projects listen on plain http. That scheme is the one thing that has to
// be spelled right in both worlds, because the identity provider stamps it into every token's
// `iss` and the MCP server validates that value. The AppHost is the one place that knows which
// world it is describing (`aspire run` versus `azd up`), so it decides here, once.
var publishedScheme = builder.ExecutionContext.IsPublishMode ? "https" : "http";

var identityServer = builder.AddProject<Projects.TravelBooking_IdentityServer>("identity-server");

// Tell IdentityServer the origin it is published on, which it cannot work out for itself:
// Aspire proxies project endpoints, so the process binds a port Aspire picked while the browser
// and the MCP server use this one. It pins its token issuer to this value, so that a token's
// `iss` does not depend on which of the two the request came through.
var identityServerUrl = identityServer.GetEndpoint(publishedScheme);
identityServer.WithEnvironment("IdentityServer__PublicOrigin", identityServerUrl);

// Labs 0-4 run the server open; Lab 5 is where it gets protected.
//
// The Host decides this by whether "Authentication:Authority" is configured at
// all (see HostBootstrap.AddTravelBookingAuth), so handing it an authority here
// unconditionally would secure /mcp from Lab 0 onward -- and an attendee's very
// first agent call would fail with a 401 before the workshop has said the word
// "OAuth". IdentityServer still starts either way, so it is visible in the
// dashboard from the beginning, which is what Lab 5 promises.
//
// The switch lives in this project's appsettings.json; Lab 5 flips it to true.
var useIdentityServer = builder.Configuration.GetValue("UseIdentityServer", false);

var travelBookingHost = builder.AddProject<Projects.TravelBooking_Host>("travelbooking-host")
    .WithReference(travelBookingDb)
    .WaitFor(travelBookingDb)
    .WaitFor(identityServer)
    // The Azure AI Foundry agent sends a W3C traceparent header. Earlier runtimes
    // sent it with the sampled flag off ("...-00"); OpenTelemetry's default
    // ParentBased sampler drops a span whose remote parent was not sampled, so
    // every trace started by the agent was silently discarded and the dashboard
    // looked empty. Today's runtime sends "...-01", but the line stays: a client
    // that sends an unsampled parent fails without a sound. Sampling everything
    // is right for a workshop; in production you would sample deliberately.
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on");

// The MCP server names itself in its protected-resource metadata, and the identity provider's
// token page prints the deploy.sh command that points the agent at it. Both default to the
// local shape; published, they have to say the Container Apps URL instead.
var travelBookingHostUrl = travelBookingHost.GetEndpoint(publishedScheme);
travelBookingHost.WithEnvironment("Authentication__ResourceUrl", travelBookingHostUrl);

if (builder.ExecutionContext.IsPublishMode)
{
    identityServer.WithEnvironment("IdentityServer__McpServerUrl", travelBookingHostUrl);

    // The identity provider refuses to start outside Development unless told, in so many
    // words, that it is wanted there (see its Program.cs). Lab 6 wants it there: deploying
    // the workshop IdP next to the server is what keeps the day's auth story alive in the
    // cloud. It stays a lab prop -- published test passwords, a developer signing key -- and
    // the lab says so. In a real system this line does not exist; Authentication__Authority
    // points at a real identity provider instead.
    identityServer.WithEnvironment("IdentityServer__AllowOutsideDevelopment", "true");

    // The published container runs as a non-root user, and /app -- where the identity
    // provider keeps its signing and data-protection keys locally -- is read-only to it.
    // Ephemeral is fine: a restart mints new keys either way, and the lab says so.
    identityServer.WithEnvironment("IdentityServer__KeysPath", "/tmp/identity-server-keys");
}

// A dev-time API browser: useful on the dashboard, no business in the cloud.
var scalar = builder.AddScalarApiReference(options =>
{
    options.AllowSelfSignedCertificates();
}).ExcludeFromManifest();
scalar.WithApiReference(travelBookingHost);

if (useIdentityServer)
{
    travelBookingHost.WithEnvironment("Authentication__Authority", identityServerUrl);
}

builder.Build().Run();
