FROM microsoft/dotnet:2-runtime
WORKDIR /srv
EXPOSE 8080
EXPOSE 40000/udp
ADD bin/Release/netcoreapp2.0/linux-x64/publish /srv
RUN chmod a+x netcode.io.demoserver
USER 33:33
ENTRYPOINT ["/srv/netcode.io.demoserver"]