package local.movementtrackerforvam.movementtrackerforvam;

import android.content.Context;
import android.view.MotionEvent;
import android.view.View;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.SeekBar;
import android.widget.TextView;

/**
 * Independent navigation input state. One finger orbits, two fingers pan and pinch.
 * Touch values are cumulative so a dropped datagram never loses a movement delta.
 * The grab overlay/buttons stay separate and keep their own hit targets.
 */
final class CameraNavigationControl implements View.OnTouchListener {
    static final class Snapshot {
        final boolean enabled, follow;
        final float yaw, pitch, panX, panY, logDolly, fov;
        final int frame;
        Snapshot(boolean enabled, boolean follow, float yaw, float pitch, float panX,
                 float panY, float logDolly, float fov, int frame) {
            this.enabled=enabled; this.follow=follow; this.yaw=yaw; this.pitch=pitch;
            this.panX=panX; this.panY=panY; this.logDolly=logDolly; this.fov=fov; this.frame=frame;
        }
    }

    final Button toggle;
    final LinearLayout lensControl;
    private final ImageView image;
    private boolean enabled, follow=true;
    private float yaw, pitch, panX, panY, logDolly, fov=60f;
    private int frame;
    private int pointers;
    private float prevX, prevY, prevSpan;

    CameraNavigationControl(Context context, ImageView image) {
        this.image=image;
        toggle=new CameraOverlayButton(context);
        toggle.setText("Orbit");
        toggle.setOnClickListener(v -> setEnabled(!enabled));
        lensControl=new LinearLayout(context);
        lensControl.setOrientation(LinearLayout.HORIZONTAL);
        lensControl.setGravity(android.view.Gravity.CENTER_VERTICAL);
        lensControl.setBackgroundColor(0x99000000);
        TextView label=new TextView(context);
        label.setText("FOV");
        label.setTextColor(0xffffffff);
        label.setPadding(8,0,8,0);
        lensControl.addView(label);
        SeekBar lens=new SeekBar(context);
        lens.setMax(100);
        lens.setProgress(50);
        lensControl.addView(lens,new LinearLayout.LayoutParams(0,CameraOverlayButton.dp(context,42),1));
        lens.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar bar,int value,boolean user) {
                if(user) synchronized(CameraNavigationControl.this) { fov=10f+value; }
            }
            @Override public void onStartTrackingTouch(SeekBar bar) {}
            @Override public void onStopTrackingTouch(SeekBar bar) {}
        });
        Button followButton=new CameraOverlayButton(context);
        followButton.setText("Follow");
        followButton.setOnClickListener(v -> {
            synchronized(CameraNavigationControl.this) { follow=!follow; }
            followButton.setText(follow ? "Follow" : "Fixed");
        });
        lensControl.addView(followButton,new LinearLayout.LayoutParams(
            CameraOverlayButton.dp(context,86),CameraOverlayButton.dp(context,42)));
        lensControl.setVisibility(View.GONE);
        image.setOnTouchListener(this);
    }

    synchronized void setEnabled(boolean value) {
        if(enabled==value) return;
        enabled=value;
        pointers=0;
        prevSpan=0f;
        toggle.setText(enabled ? "Track" : "Orbit");
        lensControl.setVisibility(enabled ? View.VISIBLE : View.GONE);
        if(enabled) frame++;
    }

    synchronized boolean isEnabled() { return enabled; }
    synchronized void frame() { frame++; }

    synchronized Snapshot snapshot() {
        return new Snapshot(enabled,follow,yaw,pitch,panX,panY,logDolly,fov,frame);
    }

    private static float span(MotionEvent event) {
        float dx=event.getX(0)-event.getX(1);
        float dy=event.getY(0)-event.getY(1);
        return (float)Math.hypot(dx,dy);
    }

    @Override public synchronized boolean onTouch(View view, MotionEvent event) {
        if(!enabled) return false;
        final int action=event.getActionMasked();
        if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL) {
            pointers=0; prevSpan=0f; return true;
        }
        if(action==MotionEvent.ACTION_DOWN||action==MotionEvent.ACTION_POINTER_DOWN
            ||action==MotionEvent.ACTION_POINTER_UP) {
            // Rebase on finger count change, never interpret the jump as a gesture.
            pointers=action==MotionEvent.ACTION_POINTER_UP ? event.getPointerCount()-1 : event.getPointerCount();
            if(action==MotionEvent.ACTION_POINTER_UP && pointers==1) {
                int remaining=event.getActionIndex()==0 ? 1 : 0;
                prevX=event.getX(remaining);
                prevY=event.getY(remaining);
            } else {
                prevX=event.getX(0); prevY=event.getY(0);
            }
            prevSpan=event.getPointerCount()>=2 ? span(event) : 0f;
            return true;
        }
        if(action!=MotionEvent.ACTION_MOVE) return true;
        int count=event.getPointerCount();
        if(count!=pointers) {
            pointers=count; prevX=event.getX(0);prevY=event.getY(0);
            prevSpan=count>=2?span(event):0f;return true;
        }
        float width=Math.max(1f,view.getWidth()), height=Math.max(1f,view.getHeight());
        if(count==1) {
            float x=event.getX(),y=event.getY();
            yaw += (x-prevX)/width*180f;
            pitch += (y-prevY)/height*150f;
            prevX=x; prevY=y;
        } else if(count>=2) {
            float x=(event.getX(0)+event.getX(1))*.5f;
            float y=(event.getY(0)+event.getY(1))*.5f;
            float previousCenterX=prevX,previousCenterY=prevY;
            // On pointer-down prevX/prevY are first finger; initialize midpoint.
            if(prevSpan<=0f) { prevX=x;prevY=y;prevSpan=span(event);return true; }
            panX -= (x-previousCenterX)/width;
            panY += (y-previousCenterY)/height;
            float currentSpan=Math.max(1f,span(event));
            logDolly -= (float)Math.log(currentSpan/Math.max(1f,prevSpan));
            prevSpan=currentSpan;prevX=x;prevY=y;
        }
        return true;
    }
}
