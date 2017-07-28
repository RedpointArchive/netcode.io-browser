FROM mono:5.0.1.1
WORKDIR /srv
EXPOSE 8080
EXPOSE 40000/udp
ENTRYPOINT ["/srv-run.sh"]
RUN apt-get update && apt-get install -y libsodium13 bash dos2unix && ldconfig && ln -s /usr/lib/x86_64-linux-gnu/libsodium.so.13 /srv/libsodium.so.18
ADD bin/Release /srv
ADD srv-run.sh /srv-run.sh
RUN dos2unix /srv-run.sh && chmod a+x /srv-run.sh && chown -Rv 33:33 /srv && chown -Rv 33:33 /srv-run.sh
USER 33:33