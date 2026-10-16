#!/bin/bash
# sound-polarity lab: identical shoot runs, one bit forced per run
run(){ tag=$1; shift; env "$@" TAG=$tag FRAMES=4000 P1MODE=shoot ./run2.sh -wavwrite /workspace/mame/runs/$tag.wav > runs/$tag.out 2>&1; }
run sl_base &
run sl_q4_1 FORCEQ=4:1 &
run sl_q4_0 FORCEQ=4:0 &
run sl_q3_1 FORCEQ=3:1 &
wait
run sl_q3_0 FORCEQ=3:0 &
run sl_q7_1 FORCEQ=7:1 &
run sl_fc2_0 FORCEFC=4:0 &
run sl_fc2_1 FORCEFC=4:4 &
wait
run sl_fc3_0 FORCEFC=8:0 &
run sl_fc3_1 FORCEFC=8:8 &
run sl_fc4_0 FORCEFC=10:0 &
run sl_fc4_1 FORCEFC=10:10 &
wait
echo ALLDONE
