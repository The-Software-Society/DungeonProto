#!/bin/bash
cd /Users/jon/Developer/AldenFall
export SSL_CERT_FILE=$(python3 -c "import certifi; print(certifi.where())")
# wait for the in-flight foliage batch to finish
while kill -0 87405 2>/dev/null; do sleep 20; done
echo "prev batch done, launching mountain/volcano generation" >> scratchpad/meshy_gen2.log
python3 tools/meshy_pipeline.py --props >> scratchpad/meshy_gen2.log 2>&1
echo "MOUNTAIN/VOLCANO GEN COMPLETE" >> scratchpad/meshy_gen2.log
