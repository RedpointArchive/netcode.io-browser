FROM mono:5.0.1.1
WORKDIR /srv
EXPOSE 8080
EXPOSE 40000/udp
ENTRYPOINT ["/srv-run.sh"]
RUN echo "deb http://ftp.debian.org/debian jessie-backports main" > /etc/apt/sources.list.d/backports.list && \
    apt-get update && \
    apt-get install -y libsodium18 bash dos2unix && \
    ldconfig
ADD bin/Release /srv
ADD srv-run.sh /srv-run.sh
RUN cp /srv/libnetcode64.so /srv/libnetcode.so && \
    dos2unix /srv-run.sh && \
    chmod a+x /srv-run.sh && \
    chown -Rv 33:33 /srv && \
    chown -Rv 33:33 /srv-run.sh
USER 33:33