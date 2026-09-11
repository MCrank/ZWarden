#!/bin/bash
# Synthetic stand-in for Project Zomboid's shipped Linux launcher, carrying only the
# parts F12 transforms: the 16g ship-default heap and a ZGC line without AlwaysPreTouch.
# NOT a real PZ artefact (ADR 0009) - a hand-written fixture.
"./jre64/bin/java" \
  -Djava.awt.headless=true -Dzomboid.steam=1 \
  -XX:+UseZGC -XX:-CreateCoredumpOnCrash \
  -Xms16g -Xmx16g \
  -Djava.library.path=natives/:natives/linux64/:. \
  -cp "java/:." zombie.network.GameServer "$@"
