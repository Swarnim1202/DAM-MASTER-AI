def q(v):
  if v is None: return 'NULL'
  if isinstance(v,(int,float)): return str(v)
  return "'"+str(v).replace("\\","\\\\").replace("'","''")+"'"
def ins(t,cols,rows):
  return f"INSERT INTO {t} ({','.join(cols)}) VALUES\n"+",\n".join("("+",".join(q(v) for v in r)+")" for r in rows)+";\n"
out=["-- DAM MASTER AI · reference catalogue seed (from RCPL-DMAI-REF-001)\nSET NAMES utf8mb4;\n",
"DELETE FROM ref_dam_type; DELETE FROM ref_dam_function; DELETE FROM ref_hazard_class; DELETE FROM ref_failure_mode; DELETE FROM ref_parameter_group; DELETE FROM ref_parameter; DELETE FROM ref_instrument_type; DELETE FROM ref_failure_mode_parameter; DELETE FROM ref_alert_level; DELETE FROM ref_interpretation;\n",
"-- standards keep any verification already recorded\n"]
dt=[('CONC_GRAVITY','Concrete gravity (conventional)','Concrete','Its own weight resists the water push','Sliding on foundation, uplift, cracking, joint opening, ageing concrete','Uplift, drain flow, pendulum / crest movement, joints, seepage, temperature'),
('RCC_GRAVITY','Roller-compacted concrete (RCC) gravity','Concrete','Gravity dam built in thin compacted layers','Seepage along lift joints, thermal cracking','Seepage per gallery zone, lift-joint wetness, temperature, uplift'),
('ARCH','Arch (single / double curvature)','Concrete','Curved shell carries load into the valley walls','Abutment movement, cracking from thermal and load cycles','Pendulums, abutment extensometers, joint meters, strain, temperature, seismic'),
('ARCH_GRAVITY','Arch-gravity','Concrete','Part arch, part weight','Combination of arch and gravity weaknesses','As arch plus uplift'),
('BUTTRESS','Buttress (flat slab / multiple arch)','Concrete','Thin face supported by buttresses','Buttress cracking, lateral stability in earthquakes, face deterioration','Crack meters, tilt, seismic, concrete condition'),
('MASONRY_GRAVITY','Masonry gravity','Masonry','Stone masonry acting by weight','Mortar leaching, seepage through masonry, uplift','Seepage quantity and chemistry, uplift, visual leaching'),
('EARTH_HOMOG','Homogeneous earthfill','Embankment','One soil type; slow seepage through the body','Internal erosion, downstream slope failure, rising phreatic line','Piezometers, seepage and turbidity, settlement, downstream wet areas'),
('EARTH_ZONED','Zoned earthfill (central / inclined core)','Embankment','Clay core with sand/gravel shells and filters','Core cracking, filter failure, piping, settlement, rapid-drawdown failure','Core and foundation piezometers, seepage, turbidity, settlement, inclinometers'),
('ECRD','Earth-core rockfill (ECRD)','Embankment','Clay core with rockfill shells','Core hydraulic fracturing, differential settlement, arching','Core pore pressure, settlement, inclinometers, total pressure, seepage'),
('CFRD','Concrete-faced rockfill (CFRD)','Embankment','Rockfill body with an upstream concrete slab','Face slab cracking, perimeter joint opening, leakage','Perimeter joint meters, slab strain, rockfill settlement, seepage weir'),
('ACRD','Asphalt-core / diaphragm rockfill','Embankment','Thin asphalt or concrete wall inside the fill','Core cracking or joint leaks','Seepage, core deformation, settlement'),
('BARRAGE_WEIR','Barrage / weir','Concrete','Raises river level for diversion with gates','Piping under floor, scour, gate failure','Uplift under floor, scour, gates, abutment seepage')]
out.append(ins('ref_dam_type',['code','name','material_group','description','weaknesses','monitoring_priority','sort_order'],[r+(i+1,) for i,r in enumerate(dt)]))
df=[('STORAGE','Storage / multipurpose','Irrigation, drinking water, power, flood control','Full instrumentation; long-term behaviour models'),('DIVERSION','Diversion (barrage / weir)','Raise level for diversion; little storage','Gates, scour, uplift under floor, foundation piping'),('DETENTION','Detention / flood control','Temporary flood storage; often empty','Instruments must work after long idle periods'),('CHECK_COFFER','Check dam / cofferdam','Small or temporary structures','Overtopping and erosion'),('TAILINGS','Tailings / ash dam','Store mining or power-plant waste','Pore pressure, phreatic level, liquefaction risk')]
out.append(ins('ref_dam_function',['code','name','description','monitoring_note','sort_order'],[r+(i+1,) for i,r in enumerate(df)]))
out.append(ins('ref_hazard_class',['code','name','description','risk_factor'],[('HIGH','High','Loss of life likely if the dam fails',1.0),('SIGNIFICANT','Significant','Major economic / environmental loss; loss of life unlikely',0.7),('LOW','Low','Limited losses, mainly to the owner',0.4)]))
fm=[('OVERTOPPING','Overtopping','Water flows over the crest; embankments wash away quickly','All; embankments most vulnerable','Reservoir rising faster than planned, gates not opening, blocked spillway'),
('PIPING','Internal erosion / piping','Seeping water carries soil particles out, forming a growing pipe','Embankment; foundations of concrete dams','Rising seepage at constant level, turbid seepage, new wet spots, boils, sinkholes'),
('SLOPE','Slope instability','Part of an embankment slides','Embankment','Bulging, slope cracks, high pore pressure, inclinometer movement'),
('SLIDING','Sliding of concrete dam','A block moves along its foundation','Gravity, buttress','Uplift above design, drains clogged, downstream movement'),
('OVERSTRESS','Overturning / overstress','Excessive tension or compression','Gravity, arch','Cracking at heel or toe, abnormal strain'),
('FOUNDATION','Foundation / abutment failure','Rock wedge or weak layer moves','Arch especially; all dams','Abutment movement, new seepage paths, rising foundation pressure'),
('CRACKING','Cracking and joint opening','Cracks let water in and start erosion','All','Growing crack width, new leakage'),
('DETERIORATION','Concrete deterioration (AAR, leaching)','Concrete swells or dissolves','Concrete, masonry','Irreversible drift, white deposits, chemistry change'),
('LIQUEFACTION','Liquefaction (earthquake)','Saturated loose soil loses strength in shaking','Embankment, tailings','Sand boils, slumping after an earthquake'),
('SPILLWAY_GATE','Spillway / gate failure','Spillway erodes or gates cannot open','Gated dams','Cavitation damage, failed gate tests, slab uplift'),
('DRAWDOWN','Rapid drawdown failure','Fast lowering leaves trapped water in the upstream slope','Embankment','Drawdown faster than design rate'),
('RIM_SLIDE','Reservoir rim landslide','Hillside slides into the reservoir, creating a wave','All','Rim cracks, movement after rain or drawdown')]
out.append(ins('ref_failure_mode',['code','name','description','applies_to','early_signs','sort_order'],[r+(i+1,) for i,r in enumerate(fm)]))
pg=[('A','Loads (hydraulic)'),('B','Seepage'),('C','Water pressure'),('D','Deformation — surface'),('E','Deformation — internal'),('F','Stress, strain, temperature'),('G','Seismic'),('H','Operational / mechanical'),('I','Visual and remote')]
out.append(ins('ref_parameter_group',['code','name','sort_order'],[r+(i+1,) for i,r in enumerate(pg)]))
pa=[('RWL','Reservoir water level','A','m',1,0,'Main load; every response is read against it'),('TWL','Tailwater level','A','m',1,0,'Downstream conditions'),('RNF','Rainfall','A','mm',0,1,'Explains seepage and pressure spikes; flood forecasting'),('INF','Inflow','A','m3/s',1,0,'Flood handling and routing'),('GTO','Spillway gate opening','H','m',0,1,'Release and gate readiness'),
('UPL','Uplift pressure','C','kPa',0,0,'Reduces sliding resistance of concrete dams'),('PWP','Pore water pressure','C','kPa',0,0,'Effective stress, slope stability, seepage path'),('PHL','Phreatic level','C','m',0,0,'Saturation line in embankments'),
('SPF','Seepage flow','B','L/min',0,0,'Most important single indicator of water through or under the dam'),('TRB','Seepage turbidity','B','NTU',0,0,'Soil being carried out = internal erosion'),('DRF','Drain flow','B','L/min',0,0,'Drain effectiveness'),
('PLD','Pendulum displacement','D','mm',0,0,'Concrete dam deflection and foundation movement'),('SET','Crest settlement','D','mm',0,0,'Settlement and overall deformation'),('TLT','Tilt','D','arc-sec',0,0,'Block rotation'),
('INC','Inclinometer displacement','E','mm',0,0,'Shear zones inside embankments'),('EXT','Extensometer displacement','E','mm',0,0,'Foundation / abutment movement'),('JNT','Joint / crack opening','E','mm',0,0,'Opening and sliding at joints or cracks'),
('TMP','Concrete temperature','F','degC',0,0,'Explains seasonal movement'),('STR','Strain','F','microstrain',0,0,'Overstress, thermal and AAR effects'),('ACC','Ground acceleration','G','g',0,1,'Earthquake loading'),('ATM','Air temperature','A','degC',0,1,'Seasonal correction')]
out.append(ins('ref_parameter',['code','name','group_code','unit','is_load','is_event','purpose'],pa))
it=[('VW_PIEZO','Vibrating-wire piezometer','Pore / uplift pressure','Pressure changes the frequency of a tensioned wire','Frequency (Hz) / digits + thermistor (Ω)','kPa','0.35–3.5 MPa; ~0.025% FS','All dams'),
('STANDPIPE','Standpipe piezometer + level sensor','Water level in a pipe','Open pipe; dip meter or transducer','Manual, 4–20 mA, Modbus','m','Pipe depth; ±1 cm','Embankments, foundations'),
('UPLIFT_GAUGE','Uplift pressure gauge (gallery)','Uplift under concrete dam','Pipe to Bourdon gauge or transducer','Manual or 4–20 mA','kPa','Full reservoir head','Gravity, buttress'),
('WEIR','V-notch weir + level sensor','Seepage flow','Head over notch converted by weir equation','4–20 mA, Modbus, SDI-12','L/min','0–hundreds of L/s','All dams'),
('FLOWMETER','Electromagnetic / ultrasonic flow meter','Drain or pipe flow','Faraday / transit time','Pulse, 4–20 mA, Modbus','L/min','Pipe-size dependent','Drains, relief wells'),
('TURBIDITY','Turbidity sensor','Cloudiness of seepage','Light scattering (nephelometric)','4–20 mA, Modbus','NTU','0–1 000 NTU','Seepage outfalls'),
('PENDULUM','Plumb / inverted pendulum + reader','Horizontal deflection (2 axes)','Optical / capacitive wire tracking','RS-485, 4–20 mA','mm','±25–50 mm; 0.01–0.1 mm','Gravity, arch'),
('INCLINOMETER','In-place inclinometer','Lateral displacement profile','MEMS / servo accelerometers','RS-485 / SDI-12','mm','±10–30°; ~0.01 mm/m','Embankments, slopes'),
('EXTENSOMETER','Rod / borehole extensometer','Axial movement','VW / potentiometric transducer','Hz / mV','mm','25–150 mm','Foundations, abutments'),
('SETTLEMENT_CELL','Settlement cell / hydraulic overflow cell','Internal settlement','Liquid level / pressure difference','Hz / mA','mm','Up to several m','Embankment, CFRD'),
('JOINT_METER','Joint / crack meter (incl. 3-D)','Opening, sliding, settlement','VW / potentiometric','Hz / mV','mm','12–100 mm; 0.01 mm','Concrete joints, CFRD'),
('TILTMETER','Tiltmeter','Rotation','MEMS / electrolytic','Digital / mA','arc-sec','±1–15°','Concrete blocks'),
('STRAIN_GAUGE','VW strain gauge','Concrete strain','Wire frequency changes with strain','Hz + thermistor','microstrain','±3 000 µε; ~1 µε','Concrete dams'),
('THERMOMETER','Thermometer / thermistor string','Temperature','Resistance','Ω','degC','−20 to 80 °C','Concrete, reservoir'),
('FIBRE_DTS','Distributed fibre-optic sensing','Temperature / strain along a cable','Light scattering in fibre','Interrogator file','degC','Metre resolution over km','Embankments, CFRD'),
('GNSS','GNSS station','3-D position','Satellite positioning','NMEA / RINEX','mm','~2–5 mm H, 5–10 mm V','Crest, abutments, rim'),
('TOTAL_STATION','Robotic total station + prisms','Coordinates of many points','Automated angle / distance','Digital','mm','~1 mm + 1 ppm','Crest, faces'),
('LEVEL_SENSOR','Reservoir / tailwater level sensor','Water level','Radar / pressure / encoder','4–20 mA, Modbus, SDI-12','m','±5–10 mm','All'),
('RAIN_GAUGE','Tipping-bucket rain gauge','Rainfall','Mechanical tips','Pulses','mm','0.2–0.5 mm per tip','All'),
('ACCELEROGRAPH','Strong-motion accelerograph','Earthquake acceleration','Force-balance / MEMS, event-triggered','100–200 samples/s records','g','±2 g','Crest, base, free-field'),
('GATE_SENSORS','Gate encoder, hoist load cell, vibration','Gate opening, force, vibration','Encoder, strain gauge, accelerometer','Digital / mA','m, kN, mm/s','Equipment-specific','Gated spillways')]
out.append(ins('ref_instrument_type',['code','name','measures','principle','raw_output','eng_unit','typical_range','used_in','sort_order'],[r+(i+1,) for i,r in enumerate(it)]))
fmp=[('OVERTOPPING','RWL','Primary'),('OVERTOPPING','INF','Primary'),('OVERTOPPING','RNF','Supporting'),('OVERTOPPING','GTO','Supporting'),
('PIPING','SPF','Primary'),('PIPING','TRB','Primary'),('PIPING','PWP','Supporting'),('PIPING','PHL','Supporting'),('PIPING','SET','Supporting'),
('SLOPE','PWP','Primary'),('SLOPE','INC','Primary'),('SLOPE','SET','Supporting'),('SLOPE','PHL','Supporting'),
('SLIDING','UPL','Primary'),('SLIDING','DRF','Primary'),('SLIDING','PLD','Primary'),('SLIDING','JNT','Supporting'),
('OVERSTRESS','STR','Primary'),('OVERSTRESS','JNT','Supporting'),('OVERSTRESS','PLD','Supporting'),
('FOUNDATION','EXT','Primary'),('FOUNDATION','UPL','Supporting'),('FOUNDATION','SPF','Supporting'),
('CRACKING','JNT','Primary'),('CRACKING','SPF','Supporting'),('DETERIORATION','PLD','Primary'),('DETERIORATION','SPF','Supporting'),
('LIQUEFACTION','ACC','Primary'),('LIQUEFACTION','PWP','Primary'),('SPILLWAY_GATE','GTO','Primary'),('DRAWDOWN','RWL','Primary'),('DRAWDOWN','PWP','Primary'),('RIM_SLIDE','INC','Primary'),('RIM_SLIDE','RNF','Supporting')]
out.append(ins('ref_failure_mode_parameter',['failure_mode_code','parameter_code','role'],fmp))
std=[('DSA2021','Dam Safety Act, 2021 (Government of India)','Specified dams; NDSA, SDSO; inspections, instrumentation, EAP, comprehensive evaluation','Law & regulation'),
('NDSA_REG','NDSA regulations and notifications','Procedures and formats under the Act','Law & regulation'),
('IS6512','IS 6512','Criteria for design of solid gravity dams','Design'),('IS1893_1','IS 1893 (Part 1)','Earthquake-resistant design — seismic zones','Design'),('IS7894','IS 7894','Stability analysis of earth dams','Design'),('IS8826','IS 8826','Design of large earth and rockfill dams','Design'),('IS12169','IS 12169','Design of small embankment dams','Design'),('IS10635','IS 10635','Freeboard requirements in embankment dams','Design'),('IS9429','IS 9429','Drainage systems for gravity dams, foundations and abutments','Design'),('IS456','IS 456','Plain and reinforced concrete','Design'),
('IS11223','IS 11223','Fixing spillway capacity','Hydraulic structures'),('IS6934','IS 6934','Hydraulic design of high ogee overflow spillways','Hydraulic structures'),('IS4997','IS 4997','Hydraulic-jump stilling basins','Hydraulic structures'),('IS7365','IS 7365','Bucket-type energy dissipators','Hydraulic structures'),('IS4623','IS 4623','Radial gates','Hydraulic structures'),('IS6938','IS 6938','Rope-drum hoists for gates','Hydraulic structures'),
('IS7436_1','IS 7436 (Part 1)','Types of measurements and choice of instruments — earth and rockfill dams','Instrumentation'),('IS7436_2','IS 7436 (Part 2)','Types of measurements and choice of instruments — concrete and masonry dams','Instrumentation'),('IS8282','IS 8282','Pore-pressure instruments in earth dams (installation and observation)','Instrumentation'),('IS4967','IS 4967','Seismic instrumentation for river valley projects','Instrumentation'),('IS10334','IS 10334','Cables for measuring devices in concrete and masonry dams','Instrumentation'),
('CWC_INSTR','CWC guidelines for instrumentation of large dams','Instrument selection, layout, frequency','CWC / DRIP'),('CWC_INSP','CWC guidelines for safety inspection of dams','Inspections and checklists','CWC / DRIP'),('CWC_EAP','CWC guidelines for developing Emergency Action Plans','Emergency levels and notification','CWC / DRIP'),('CWC_RISK','CWC guidelines for assessing and managing risks associated with dams','Risk assessment, PFMA','CWC / DRIP'),
('ICOLD_SURV','ICOLD bulletins on dam surveillance','Surveillance principles, behaviour models','International'),('USACE_INSTR','USACE manuals on instrumentation of embankment and concrete structures','Instrument practice','International'),('FEMA_FGDS','FEMA Federal Guidelines for Dam Safety','Hazard classification, EAP','International')]
out.append("INSERT INTO ref_standard (code,title,subject,category,sort_order) VALUES\n"+",\n".join("("+",".join(q(v) for v in r+(i+1,))+")" for i,r in enumerate(std))+"\nON DUPLICATE KEY UPDATE title=VALUES(title),subject=VALUES(subject),category=VALUES(category),sort_order=VALUES(sort_order);\n")
al=[(0,'Normal','#12946A','Behaving as expected','—','Dashboard',0),(1,'Alert','#D98A1C','Unusual behaviour — needs attention','InstrumentationEngineer,DamSafetyEngineer','In-app, e-mail',1440),(2,'Alarm','#E06B1A','Unsafe trend or design limit reached','DamSafetyEngineer,Approver,Management','In-app, e-mail, SMS',120),(3,'Emergency','#D14545','Failure possible or developing — activate EAP','DamSafetyEngineer,Approver,Management,Admin','All channels',15)]
out.append(ins('ref_alert_level',['level','name','colour','meaning','notify_roles','channels','ack_minutes'],al))
ip=[('B','Seepage flow','Rises and falls with reservoir level along a repeatable curve','Higher than usual for the same level; steady rise at constant level','Rapid or accelerating rise; new concentrated leak','Cracking, joint opening, curtain deterioration, internal erosion','Increase readings, inspect, compare piezometers, sample water'),
('B','Turbidity','Clear water, low stable NTU','Occasional cloudiness after rain or maintenance','Persistent or increasing cloudiness, soil in weir box','Soil being transported — internal erosion','Treat as possible piping: immediate inspection, EAP readiness'),
('B','Sudden drop in seepage','—','Flow drops without reservoir fall','Drop plus rising pore pressure behind','Drains clogging, flow taking another path','Check drains; pressure may be building'),
('C','Embankment pore pressure','Follows reservoir with a lag; stable relation','Higher than history for the same level; faster response','Downstream piezometers rising toward slope surface','Core / filter defect, blocked drain, new seepage path','Slope stability check, more readings, check drains'),
('C','Uplift under concrete dam','Matches design uplift diagram','Above design at drain line; drain flow falling','Uplift close to full reservoir head','Drains clogged, curtain deteriorated, foundation cracking','Clean / re-drill drains; recompute sliding factor of safety'),
('C','Rapid drawdown','—','Lowering faster than design rate','Upstream pore pressures stay high while level falls','Trapped water in upstream slope','Slow the drawdown; slope check'),
('D','Concrete dam deflection','Repeatable seasonal loop','Loop drifting downstream year by year','Sudden step or accelerating drift','Creep, AAR, foundation movement, sliding','HST analysis, foundation investigation'),
('D','Crest settlement','Slows down with time after construction','Rate not decreasing','Accelerating settlement, depression, crest cracks','Internal erosion, collapse settlement, slope movement','Survey more often, inspect, check freeboard'),
('E','Inclinometer profile','Small uniform movement','Movement concentrated at one depth','Growing shear zone, accelerating','Developing slip surface','Emergency evaluation; lower reservoir if needed'),
('E','Joint / crack opening','Seasonal opening and closing','Progressive opening year on year','Rapid opening or new cracking with leakage','Differential movement, overstress','Structural assessment, sealing'),
('A','Reservoir level','Within rule curve','Above rule curve; rising faster than forecast','Approaching MWL with gates fully open','Large inflow, gate problem','Pre-release, flood watch, EAP if overtopping possible'),
('G','Earthquake','No event','Felt shaking or PGA above inspection trigger','Strong shaking with new cracks, seepage or settlement','Seismic loading','Special post-earthquake inspection and readings'),
('H','Spillway gates','Pass operation tests','Failed test, hoist overload, vibration','Gate cannot open during flood; no standby power','Mechanical / electrical fault','Repair; alternative release; EAP')]
out.append(ins('ref_interpretation',['group_code','observation','normal_pattern','warning_sign','critical_sign','possible_cause','action_text'],ip))
open('002_seed_reference.sql','w').write(''.join(out));print('ok')
