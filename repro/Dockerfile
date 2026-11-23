FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY app/ app/
COPY counter/ counter/
RUN dotnet publish app/SocketRepro.csproj -c Release -p:UseAppHost=false -o /published/app && dotnet publish counter/ProcessProbe.csproj -c Release -o /published/counter

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS net10
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0
COPY --from=net10 /usr/share/dotnet/ /opt/net10/
COPY --from=net10 /usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.12/ /usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.12/
COPY --from=build /published/ /measure/
ENV DOTNET_ROLL_FORWARD=Major
WORKDIR /measure/app
