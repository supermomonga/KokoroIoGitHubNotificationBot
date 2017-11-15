FROM microsoft/dotnet:2.0.0-sdk-jessie AS builder
MAINTAINER supermomonga

ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=-1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=-1

WORKDIR /app/src/KokoroIoGitHubNotificationBot
COPY ./KokoroIoGitHubNotificationBot/KokoroIoGitHubNotificationBot.csproj ./KokoroIoGitHubNotificationBot/KokoroIoGitHubNotificationBot.jsproj
COPY ./KokoroIoGitHubNotificationBot.sln ./KokoroIoGitHubNotificationBot.sln
RUN dotnet restore
COPY ./KokoroIoGitHubNotificationBot ./KokoroIoGitHubNotificationBot
RUN dotnet publish -c Release -o /app/bin -p:PublishWithAspNetCoreTargetManifest=false


FROM microsoft/dotnet:2.0.0-runtime-jessie
WORKDIR /app
COPY --from=BUILDER /app/bin /app
RUN ls
ENV ASPNETCORE_URLS http://+:5000
ENTRYPOINT ["dotnet", "KokoroIoGitHubNotificationBot.dll"]

