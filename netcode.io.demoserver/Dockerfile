FROM mono:5.0.1.1
ADD bin/Release /srv
WORKDIR /srv
EXPOSE 8080
EXPOSE 40000/udp
CMD ["mono", "netcode.io.demoserver.exe"]