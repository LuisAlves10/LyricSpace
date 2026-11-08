(function () {
    var root = new File($.fileName).parent;
    var result = new File(root.fsName + "/ae-native-world.json");
    var error = new File(root.fsName + "/probe-error.txt");
    var target = new File(root.fsName + "/Referencia.aep");
    function write(file, text) {
        file.encoding = "UTF-8";
        if (!file.open("w")) throw new Error("Cannot open " + file.name);
        file.write(text);
        file.close();
    }
    function array(value) {
        return "[" + value[0] + "," + value[1] + "," + value[2] + "]";
    }
    var opened = false;
    var cleanInstance = false;
    try {
        cleanInstance = !app.project || (!app.project.file && app.project.numItems === 0);
        if (!cleanInstance) throw new Error("Instance is already in use");
        app.exitAfterLaunchAndEval = true;
        app.beginSuppressDialogs();
        app.open(target);
        opened = true;
        var comp = null;
        for (var i = 1; i <= app.project.numItems; i++) {
            if (app.project.item(i).id === 704) comp = app.project.item(i);
        }
        if (!comp || !(comp instanceof CompItem)) throw new Error("Composition 704 not found in saved copy");
        var layers = [];
        for (var i = 30; i <= 35; i++) layers.push(comp.layer(i));
        var probe = comp.layers.addNull();
        probe.threeDLayer = true;
        probe.enabled = false;
        var p = probe.property("ADBE Transform Group").property("ADBE Position");
        var start = 212, end = 429;
        var fps = comp.frameRate;
        function tf(frame) { return frame / fps - comp.displayStartTime; }
        function measure(expression, frame) {
            p.expression = expression;
            var value = p.valueAtTime(tf(frame), false);
            if (p.expressionError) throw new Error(p.expressionError);
            return value;
        }
        var descriptors = ["toWorldVec([1,0,0],time)","toWorldVec([0,1,0],time)","toWorldVec([0,0,1],time)","toWorld([0,0,0],time)"];
        var rows = [];
        for (var i = 0; i < layers.length; i++) {
            var layer = layers[i];
            var channels = [];
            for (var k = 0; k < descriptors.length; k++) {
                var values = [];
                p.expression = "thisComp.layer(" + layer.index + ")." + descriptors[k];
                for (var f = start; f <= end; f++) values.push(array(p.valueAtTime(tf(f), false)));
                if (p.expressionError) throw new Error(p.expressionError);
                channels.push("[" + values.join(",") + "]");
            }
            var transforms = layer.property("ADBE Transform Group");
            var poseZ = transforms.property("ADBE Rotate Z");
            var posePosition = transforms.property("ADBE Position");
            var zoom = i === 5 ? layer.property("ADBE Camera Options Group").property("ADBE Camera Zoom") : null;
            var locals = [];
            for (var f = start; f <= end; f++) {
                locals.push("[" + array(posePosition.valueAtTime(tf(f), false)) + "," +
                    poseZ.valueAtTime(tf(f), false) + "," + (zoom ? zoom.valueAtTime(tf(f), false) : 0) + "]");
            }
            rows.push('{"id":' + layer.id + ',"world":[' + channels.join(",") + '],"locals":[' + locals.join(",") + ']}');
        }
        var camera = layers[5];
        var targetFrames = [272, 340, 422];
        var projectionRows = [];
        for (var i = 0; i < targetFrames.length; i++) {
            var worldPoint = measure("thisComp.layer(" + camera.index + ").toWorld([130,-70,1300],time)", targetFrames[i]);
            var anchor = comp.layers.addNull();
            anchor.threeDLayer = true;
            anchor.enabled = false;
            anchor.property("ADBE Transform Group").property("ADBE Position").setValue(worldPoint);
            var points = [];
            p.expression = "thisComp.layer(" + anchor.index + ").toComp(thisComp.layer(" + anchor.index + ").transform.anchorPoint,time)";
            for (var f = start; f <= end; f++) points.push(array(p.valueAtTime(tf(f), false)));
            if (p.expressionError) throw new Error(p.expressionError);
            projectionRows.push('{"world":' + array(worldPoint) + ',"values":[' + points.join(",") + ']}');
        }
        write(result, '{"format":1,"compositionId":704,"width":'+comp.width+',"height":'+comp.height+
            ',"firstFrame":' + start + ',"lastFrame":' + end + ',"fps":' + fps +
            ',"layers":[' + rows.join(",") + '],"points":[' + projectionRows.join(",") + ']}');
    } catch (e) {
        try { write(error, e.toString() + " at line " + e.line); } catch (_) {}
    } finally {
        if (opened && app.project.file && app.project.file.fsName === target.fsName) {
            app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES);
        }
        if (cleanInstance) app.endSuppressDialogs(false);
    }
}());
